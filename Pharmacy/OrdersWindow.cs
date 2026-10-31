using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Pharmacy;

public sealed class OrdersWindow : Window
{
    private readonly Store store;
    private readonly StackPanel list = new();

    public OrdersWindow(Store store)
    {
        Store.Require(store.CanManage);
        this.store = store;
        Ui.Setup(this, "Заказы");
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        top.Children.Add(Ui.Text("Заказы", 28));
        if (store.IsAdmin)
            top.Children.Add(Ui.Button("Добавить заказ", () => { Edit(null); Refresh(); }));
        top.Children.Add(Ui.Button("Назад в каталог", Close));
        root.Children.Add(top);
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Refresh();
    }

    private void Refresh()
    {
        list.Children.Clear();
        foreach (var row in store.Db.Run("SELECT o.Id,u.FullName,o.Address,s.Name,o.CreatedAt,COALESCE(o.DeliveryAt,''),COALESCE(SUM(i.Quantity*i.UnitPriceCents),0) FROM Orders o JOIN Users u ON u.Id=o.UserId JOIN OrderStatuses s ON s.Id=o.StatusId LEFT JOIN OrderItems i ON i.OrderId=o.Id GROUP BY o.Id ORDER BY o.Id DESC"))
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(Ui.Text($"Заказ №{row[0]} · {row[3]} · {decimal.Parse(row[6]) / 100:N2} ₽", 22));
            panel.Children.Add(Ui.Text(row[1] + "\n" + row[2]));
            panel.Children.Add(Ui.Text("Создан: " + row[4] + " · Доставка: " + row[5], 14));
            foreach (var line in store.Db.Run("SELECT p.Name,i.Quantity,i.UnitPriceCents FROM OrderItems i JOIN Products p ON p.Id=i.ProductId WHERE i.OrderId=?", row[0]))
                panel.Children.Add(Ui.Text($"{line[0]} — {line[1]} шт. × {int.Parse(line[2]) / 100m:N2} ₽", 14));
            if (store.IsAdmin)
            {
                var buttons = new WrapPanel();
                buttons.Children.Add(Ui.Button("Редактировать", () => { Edit(int.Parse(row[0])); Refresh(); }));
                buttons.Children.Add(Ui.Button("Удалить заказ", () =>
                {
                    Store.Require(store.IsAdmin);
                    if (!Ui.Confirm($"Удалить заказ №{row[0]} и все его позиции? История будет удалена безвозвратно. Остатки на складе не изменятся."))
                        return;
                    store.Db.Run("DELETE FROM Orders WHERE Id=?", row[0]);
                    Refresh();
                }));
                panel.Children.Add(buttons);
            }
            list.Children.Add(new Border { Child = panel, Background = Ui.Secondary, Margin = new Thickness(4, 8, 4, 8), CornerRadius = new CornerRadius(8) });
        }
    }

    private void Edit(int? id)
    {
        Store.Require(store.IsAdmin);
        var window = new Window { Owner = this };
        Ui.Setup(window, id == null ? "Добавление заказа" : "Редактирование заказа", 850, 800);
        var panel = new StackPanel { Margin = new Thickness(24) };
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var original = id == null ? null : store.Db.Run("SELECT UserId,Address,StatusId,CreatedAt,COALESCE(DeliveryAt,'') FROM Orders WHERE Id=?", id)[0];
        var users = store.Db.Run("SELECT Id,FullName FROM Users ORDER BY FullName");
        var statuses = store.Db.Run("SELECT Id,Name FROM OrderStatuses ORDER BY Id");
        panel.Children.Add(Ui.Text("Покупатель"));
        var user = new ComboBox { ItemsSource = users.Select(r => r[1]).ToList(), SelectedIndex = original == null ? 0 : users.FindIndex(r => r[0] == original[0]), Margin = new Thickness(4), Padding = new Thickness(8) };
        panel.Children.Add(user);
        var address = Ui.Field(panel, "Адрес доставки", original?[1] ?? "");
        panel.Children.Add(Ui.Text("Статус"));
        var status = new ComboBox { ItemsSource = statuses.Select(r => r[1]).ToList(), SelectedIndex = original == null ? 0 : statuses.FindIndex(r => r[0] == original[2]), Margin = new Thickness(4), Padding = new Thickness(8) };
        panel.Children.Add(status);
        var delivery = Ui.Field(panel, "Дата доставки (гггг-мм-дд чч:мм, можно оставить пустой)", original?[4] ?? "");
        panel.Children.Add(Ui.Text("Состав заказа: количество 0 исключает товар", 20));
        panel.Children.Add(Ui.Text("При редактировании сохраняется цена существующих позиций. Изменение административного заказа не изменяет складские остатки.", 13));
        var existing = id == null ? new Dictionary<int, (int Quantity, int Price)>() : store.Db.Run("SELECT ProductId,Quantity,UnitPriceCents FROM OrderItems WHERE OrderId=?", id).ToDictionary(r => int.Parse(r[0]), r => (Quantity: int.Parse(r[1]), Price: int.Parse(r[2])));
        var quantities = new List<(Product Product, TextBox Input)>();
        foreach (var product in store.Products())
            quantities.Add((product, Ui.Field(panel, product.Name + " · " + product.PriceText, existing.GetValueOrDefault(product.Id).Quantity.ToString())));
        panel.Children.Add(Ui.Button("Сохранить заказ", () =>
        {
            Store.Require(store.IsAdmin);
            if (string.IsNullOrWhiteSpace(address.Text) || user.SelectedIndex < 0 || status.SelectedIndex < 0)
                throw new InvalidOperationException("Укажите покупателя, адрес и статус заказа.");
            DateTime? deliveryDate = null;
            if (!string.IsNullOrWhiteSpace(delivery.Text))
            {
                if (!DateTime.TryParseExact(delivery.Text.Trim(), new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    throw new InvalidOperationException("Дата доставки должна иметь формат 2026-11-01 15:30.");
                deliveryDate = parsed;
                var createdAt = original == null ? DateTime.Now : DateTime.Parse(original[3], CultureInfo.InvariantCulture);
                if (deliveryDate < createdAt)
                    throw new InvalidOperationException("Дата доставки не может быть раньше даты создания заказа.");
            }
            var lines = new List<(Product Product, int Quantity)>();
            foreach (var pair in quantities)
            {
                if (!int.TryParse(pair.Input.Text, out var quantity) || quantity < 0 || quantity > 100000)
                    throw new InvalidOperationException("Количество — целое число от 0 до 100 000: " + pair.Product.Name);
                if (quantity > 0)
                    lines.Add((pair.Product, quantity));
            }
            if (lines.Count == 0)
                throw new InvalidOperationException("Добавьте хотя бы одну позицию в заказ.");
            store.Db.Transaction(() =>
            {
                var savedId = id ?? 0;
                if (id == null)
                {
                    store.Db.Run("INSERT INTO Orders(UserId,Address,StatusId,CreatedAt,DeliveryAt) VALUES(?,?,?,?,?)", users[user.SelectedIndex][0], address.Text.Trim(), statuses[status.SelectedIndex][0], DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), deliveryDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    savedId = store.Db.Scalar("SELECT last_insert_rowid()");
                }
                else
                {
                    store.Db.Run("UPDATE Orders SET UserId=?,Address=?,StatusId=?,DeliveryAt=? WHERE Id=?", users[user.SelectedIndex][0], address.Text.Trim(), statuses[status.SelectedIndex][0], deliveryDate?.ToString("yyyy-MM-dd HH:mm:ss"), id);
                    store.Db.Run("DELETE FROM OrderItems WHERE OrderId=?", id);
                }
                foreach (var line in lines)
                    store.Db.Run("INSERT INTO OrderItems VALUES(?,?,?,?)", savedId, line.Product.Id, line.Quantity, existing.TryGetValue(line.Product.Id, out var old) ? old.Price : line.Product.SaleCents);
            });
            window.DialogResult = true;
        }));
        panel.Children.Add(Ui.Button("Назад без сохранения", window.Close));
        window.ShowDialog();
    }
}
