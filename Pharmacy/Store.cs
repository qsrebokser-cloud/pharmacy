using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pharmacy;

public record User(int Id, string FullName, string Role);
public record Product(int Id, string Name, int TypeId, string Type, string Description, string Composition, int PriceCents, int Discount, bool DiscountActive, int MinStock, int Stock, string ImagePath)
{
    public int SaleCents => DiscountActive ? (int)Math.Round(PriceCents * (100m - Discount) / 100m, MidpointRounding.AwayFromZero) : PriceCents;
    public string PriceText => $"{SaleCents / 100m:N2} ₽";
}

public sealed class Store : IDisposable
{
    public Sqlite Db
    {
        get;
    }
    public string Root
    {
        get;
    }
    public User? CurrentUser
    {
        get; private set;
    }
    public Dictionary<int, int> Cart { get; } = new();
    public bool CanManage => CurrentUser?.Role is "Менеджер" or "Администратор";
    public bool IsAdmin => CurrentUser?.Role == "Администратор";

    public Store(string root, string? databaseFile = null)
    {
        Root = root;
        Db = new Sqlite(databaseFile ?? Path.Combine(root, "pharmacy.db"));
        try
        {
            foreach (var statement in File.ReadAllText(Path.Combine(root, "Database.sql")).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Db.Run(statement);
            if (Db.Scalar("SELECT COUNT(*) FROM Users") == 0)
                Import();
        }
        catch
        {
            Db.Dispose();
            throw;
        }
    }

    private static int Number(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    private static int Cents(string value) => (int)Math.Round(decimal.Parse(value, CultureInfo.InvariantCulture) * 100m, MidpointRounding.AwayFromZero);

    private void Import()
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(File.ReadAllText(Path.Combine(Root, "Import", "seed.json")))!;
        Db.Transaction(() =>
        {
            foreach (var row in data["users"].Skip(1))
            {
                Db.Run("INSERT OR IGNORE INTO Roles(Name) VALUES(?)", row["F"]);
                Db.Run("INSERT INTO Users VALUES(?,?,?,?,?,(SELECT Id FROM Roles WHERE Name=?))", row["A"], row["B"], row["C"], HashPassword(row["D"]), row["E"], row["F"]);
            }
            foreach (var row in data["products"].Skip(1))
            {
                Db.Run("INSERT OR IGNORE INTO ProductTypes(Name) VALUES(?)", row["C"]);
                var original = Path.GetFileNameWithoutExtension(row["K"]);
                var image = Directory.GetFiles(Path.Combine(Root, "Import", "import"), "*.jpg").FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Equals(original, StringComparison.OrdinalIgnoreCase));
                Db.Run("INSERT INTO Products VALUES(?,?,(SELECT Id FROM ProductTypes WHERE Name=?),?,?,?,?,?,?,?,?)", row["A"], row["B"], row["C"], row["D"], row["E"], Cents(row["F"]), row["G"], row["H"], row["I"], row["J"], image == null ? "" : Path.GetRelativePath(Root, image));
            }
            // The workbook has one row per order line, without an order ID.
            // Group by all header fields so separate orders do not get merged.
            foreach (var group in data["orders"].Skip(1).GroupBy(r => string.Join("\u001f", new[] { r["A"], r["C"], r["E"], r["F"], r["G"] })))
            {
                var row = group.First();
                Db.Run("INSERT OR IGNORE INTO OrderStatuses(Name) VALUES(?)", row["E"]);
                Db.Run("INSERT INTO Orders(UserId,Address,StatusId,CreatedAt,DeliveryAt) VALUES((SELECT Id FROM Users WHERE Login=?),?,(SELECT Id FROM OrderStatuses WHERE Name=?),?,?)", row["C"], row["A"], row["E"], row["F"], row["G"]);
                var id = Db.Scalar("SELECT last_insert_rowid()");
                foreach (var line in group)
                {
                    if (Cents(line["L"]) != Cents(line["K"]) * Number(line["J"]))
                        throw new InvalidDataException("Не совпадает сумма импортируемой строки заказа.");
                    Db.Run("INSERT INTO OrderItems VALUES(?,(SELECT p.Id FROM Products p JOIN ProductTypes t ON t.Id=p.TypeId WHERE p.Name=? AND t.Name=?),?,?)", id, line["H"], line["I"], line["J"], Cents(line["K"]));
                }
            }
        });
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    public bool Login(string login, string password)
    {
        var row = Db.Run("SELECT u.Id,u.FullName,r.Name,u.PasswordHash FROM Users u JOIN Roles r ON r.Id=u.RoleId WHERE u.Login=?", login.Trim()).FirstOrDefault();
        if (row == null)
            return false;
        var parts = row[3].Split(':');
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[0]), 100000, HashAlgorithmName.SHA256, 32);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(parts[1])))
            return false;
        Cart.Clear();
        CurrentUser = new User(Number(row[0]), row[1], row[2]);
        return true;
    }

    public void Logout()
    {
        CurrentUser = null;
        Cart.Clear();
    }

    public List<Product> Products() => Db.Run("SELECT p.Id,p.Name,p.TypeId,t.Name,p.Description,p.Composition,p.PriceCents,p.Discount,p.DiscountActive,p.MinStock,p.Stock,p.ImagePath FROM Products p JOIN ProductTypes t ON t.Id=p.TypeId").Select(r => new Product(Number(r[0]), r[1], Number(r[2]), r[3], r[4], r[5], Number(r[6]), Number(r[7]), r[8] == "1", Number(r[9]), Number(r[10]), r[11])).ToList();

    public void AddToCart(Product product)
    {
        Require(CurrentUser != null);
        var fresh = Products().Single(p => p.Id == product.Id);
        if (Cart.GetValueOrDefault(product.Id) >= fresh.Stock)
            throw new InvalidOperationException("На складе недостаточно товара. Уменьшите количество в корзине.");
        Cart[product.Id] = Cart.GetValueOrDefault(product.Id) + 1;
    }

    public int Checkout(string address)
    {
        Require(CurrentUser != null);
        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException("Введите адрес доставки.");
        if (Cart.Count == 0)
            throw new InvalidOperationException("Корзина пуста. Добавьте товары из каталога.");
        var orderId = 0;
        Db.Transaction(() =>
        {
            Db.Run("INSERT INTO Orders(UserId,Address,StatusId,CreatedAt) VALUES(?,?,(SELECT Id FROM OrderStatuses WHERE Name='Новый'),?)", CurrentUser!.Id, address.Trim(), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            orderId = Db.Scalar("SELECT last_insert_rowid()");
            foreach (var pair in Cart)
            {
                var product = Products().SingleOrDefault(p => p.Id == pair.Key);
                if (product == null || pair.Value > product.Stock)
                    throw new InvalidOperationException("Остатки изменились. Проверьте корзину.");
                Db.Run("INSERT INTO OrderItems VALUES(?,?,?,?)", orderId, product.Id, pair.Value, product.SaleCents);
                Db.Run("UPDATE Products SET Stock=Stock-? WHERE Id=?", pair.Value, product.Id);
            }
        });
        Cart.Clear();
        return orderId;
    }

    public void SaveProduct(Product product)
    {
        Require(CanManage);
        if (string.IsNullOrWhiteSpace(product.Name) || product.PriceCents < 0 || product.Discount is < 0 or > 100 || product.Stock < 0 || product.MinStock < 0)
            throw new InvalidOperationException("Проверьте название, цену, скидку и остатки.");
        if (product.Id == 0)
            Db.Run("INSERT INTO Products VALUES((SELECT COALESCE(MAX(Id),0)+1 FROM Products),?,?,?,?,?,?,?,?,?,?)", product.Name, product.TypeId, product.Description, product.Composition, product.PriceCents, product.Discount, product.DiscountActive ? 1 : 0, product.MinStock, product.Stock, product.ImagePath);
        else
            Db.Run("UPDATE Products SET Name=?,TypeId=?,Description=?,Composition=?,PriceCents=?,Discount=?,DiscountActive=?,MinStock=?,Stock=?,ImagePath=? WHERE Id=?", product.Name, product.TypeId, product.Description, product.Composition, product.PriceCents, product.Discount, product.DiscountActive ? 1 : 0, product.MinStock, product.Stock, product.ImagePath, product.Id);
    }

    public void DeleteProduct(Product product)
    {
        Require(CanManage);
        if (Db.Scalar("SELECT COUNT(*) FROM OrderItems WHERE ProductId=?", product.Id) > 0)
            throw new InvalidOperationException("Товар используется в заказах. Удаление нарушит историю заказов. Можно изменить его данные или установить остаток 0.");
        Db.Run("DELETE FROM Products WHERE Id=?", product.Id);
        Cart.Remove(product.Id);
    }

    public static void Require(bool condition)
    {
        if (!condition)
            throw new UnauthorizedAccessException("У вашей учётной записи нет прав на это действие.");
    }

    public void Dispose() => Db.Dispose();
}
