using System.IO;

namespace Pharmacy;

public static class Checks
{
    public static void Run(string root)
    {
        var file = Path.Combine(root, "check-" + Guid.NewGuid().ToString("N") + ".db");
        var report = new List<string>();
        void Assert(bool condition, string message)
        {
            if (!condition)
                throw new Exception("FAIL: " + message);
            report.Add("PASS: " + message);
        }
        void Denied(Action action, string message)
        {
            var denied = false;
            try
            {
                action();
            }
            catch (UnauthorizedAccessException) { denied = true; }
            Assert(denied, message);
        }
        try
        {
            using (var store = new Store(root, file))
            {
                Assert(store.Products().Count == 15, "Imported 15 products");
                Assert(store.Db.Scalar("SELECT COUNT(*) FROM Users") == 8, "Imported 8 users");
                Assert(store.Db.Scalar("SELECT COUNT(*) FROM Orders") == 7, "Grouped 7 orders");
                Assert(store.Db.Scalar("SELECT COUNT(*) FROM OrderItems") == 12, "Imported 12 order lines");
                Assert(store.Db.Run("PRAGMA foreign_key_check").Count == 0, "Foreign keys valid");
                Assert(store.Db.Run("PRAGMA integrity_check")[0][0] == "ok", "Database integrity");
                Denied(() => store.AddToCart(store.Products()[0]), "Guest cannot order");
                Assert(!store.Login("admin", "incorrect"), "Incorrect password rejected");
                Assert(!store.Login("' OR 1=1 --", "x"), "SQL injection rejected");
                Assert(store.Login("ivanov_ps", "user123"), "Customer login");
                var product = store.Products()[0];
                Assert(product.SaleCents == 35700, "Discount uses integer cents");
                Denied(() => store.SaveProduct(product), "Customer cannot edit products");
                store.AddToCart(product);
                var before = store.Db.Scalar("SELECT COUNT(*) FROM Orders");
                try
                {
                    store.Checkout(" ");
                }
                catch (InvalidOperationException) { }
                Assert(store.Cart.Count == 1 && store.Db.Scalar("SELECT COUNT(*) FROM Orders") == before, "Empty address preserves cart and database");
                store.Db.Run("UPDATE Products SET Stock=0 WHERE Id=?", product.Id);
                try
                {
                    store.Checkout("Test address");
                }
                catch (InvalidOperationException) { }
                Assert(store.Cart.Count == 1 && store.Db.Scalar("SELECT COUNT(*) FROM Orders") == before, "Insufficient stock rolls back entire order");
                store.Db.Run("UPDATE Products SET Stock=? WHERE Id=?", product.Stock, product.Id);
                var id = store.Checkout("Test address");
                Assert(store.Cart.Count == 0 && store.Db.Scalar("SELECT Stock FROM Products WHERE Id=?", product.Id) == product.Stock - 1, "Checkout clears cart and decrements stock");
                Assert(store.Db.Scalar("SELECT UnitPriceCents FROM OrderItems WHERE OrderId=?", id) == 35700, "Order preserves discounted price");
                store.Logout();
                Assert(store.CurrentUser == null, "Logout returns guest");
                Assert(store.Login("manager", "manager123") && store.CanManage && !store.IsAdmin, "Manager permissions");
                var newProduct = product with
                {
                    Id = 0,
                    Name = "Check product",
                    ImagePath = ""
                };
                store.SaveProduct(newProduct);
                var inserted = store.Products().Single(p => p.Name == "Check product");
                Assert(inserted.Id == 16, "Product ID equals max plus one");
                store.SaveProduct(inserted with
                {
                    PriceCents = 12345
                });
                Assert(store.Products().Single(p => p.Id == inserted.Id).PriceCents == 12345, "Product update persisted");
                store.DeleteProduct(inserted);
                Assert(store.Products().Count == 15, "Product deletion persisted");
                var blocked = false;
                try
                {
                    store.DeleteProduct(product);
                }
                catch (InvalidOperationException) { blocked = true; }
                Assert(blocked, "Order history prevents product deletion");
                Assert(store.Login("admin", "admin123") && store.IsAdmin, "Administrator login");
            }
            using (var reopened = new Store(root, file))
                Assert(reopened.Db.Scalar("SELECT COUNT(*) FROM Orders") == 8, "Data survives reopening without duplicate import");
            File.WriteAllLines(Path.Combine(root, "checks.txt"), report);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException) { }
        }
    }
}
