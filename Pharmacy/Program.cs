using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Pharmacy;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        using var mutex = new Mutex(true, args.Contains("--capture") || args.Contains("--check") ? "Local.ZharPtitsa.Pharmacy.Test" : "Local.ZharPtitsa.Pharmacy", out var first);
        if (!first)
        {
            MessageBox.Show("Приложение уже запущено.", "Жар-птица", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var root = AppContext.BaseDirectory;
            if (args.Contains("--check"))
            {
                Checks.Run(root);
                return;
            }
            using var store = new Store(root);
            var app = new Application();
            app.Resources[typeof(Button)] = new Style(typeof(Button))
            {
                Setters =
                {
                    new Setter(Control.PaddingProperty, new Thickness(16, 9, 16, 9)),
                    new Setter(FrameworkElement.MarginProperty, new Thickness(4)),
                    new Setter(Control.BackgroundProperty, Ui.Accent),
                    new Setter(Control.ForegroundProperty, Brushes.White),
                    new Setter(Control.BorderThicknessProperty, new Thickness(0)),
                    new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand)
                }
            };
            var main = new MainWindow(store);
            if (args.Contains("--capture"))
                main.ContentRendered += (_, _) => main.Dispatcher.BeginInvoke(() =>
                {
                    Ui.Guard(() => main.CaptureDocumentation());
                    app.Shutdown();
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            app.Run(main);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "error.log"), error.ToString());
            if (args.Contains("--check"))
            {
                Environment.ExitCode = 1;
                return;
            }
            MessageBox.Show("Не удалось выполнить операцию: " + error.Message + "\nПодробности в error.log.", "Ошибка приложения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public static class Ui
{
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x35, 0x5C, 0xBD));
    public static readonly Brush Secondary = new SolidColorBrush(Color.FromRgb(0xD2, 0xDF, 0xFF));
    public static readonly Brush Discount = new SolidColorBrush(Color.FromRgb(0xBB, 0x5F, 0xCA));

    public static void Setup(Window window, string title, double width = 1050, double height = 760)
    {
        window.Title = "Жар-птица — " + title;
        window.Width = width;
        window.Height = height;
        window.MinWidth = Math.Min(width, 760);
        window.MinHeight = Math.Min(height, 550);
        window.FontFamily = new FontFamily("Candara");
        window.FontSize = 16;
        window.Background = Brushes.White;
        window.Foreground = new SolidColorBrush(Color.FromRgb(0x19, 0x29, 0x48));
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Icon = LoadImage(Path.Combine(AppContext.BaseDirectory, "Import/import/IconVe.ico"));
    }

    public static Button Button(string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => Guard(action);
        return button;
    }

    public static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Ошибка — проверьте данные", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static TextBlock Text(string text, double size = 16) => new() { Text = text, FontSize = size, Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap };

    public static BitmapImage LoadImage(string file)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(Path.GetFullPath(file));
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static Image ProductImage(Store store, string path, double width = 145, double height = 112)
    {
        BitmapSource source;
        try
        {
            source = LoadImage(Path.Combine(store.Root, string.IsNullOrEmpty(path) ? "Import/import/picture.png" : path));
        }
        catch
        {
            source = LoadImage(Path.Combine(store.Root, "Import/import/picture.png"));
        }
        return new Image { Source = source, Width = width, Height = height, Stretch = Stretch.Uniform, Margin = new Thickness(8) };
    }

    public static TextBox Field(Panel panel, string label, string value = "", bool multiline = false)
    {
        panel.Children.Add(Text(label));
        var input = new TextBox { Text = value, Padding = new Thickness(8), Margin = new Thickness(4, 0, 4, 10), MinHeight = multiline ? 65 : 36, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(input);
        return input;
    }

    public static bool Confirm(string text) => MessageBox.Show(text, "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}

public sealed class MainWindow : Window
{
    private readonly Store store;
    private readonly DockPanel layout = new();
    private readonly StackPanel rows = new();
    private readonly TextBlock count = Ui.Text("");
    private readonly TextBox search = new() { MinWidth = 230, Padding = new Thickness(9), Margin = new Thickness(4), ToolTip = "Поиск по названию, типу, описанию, составу и пути к фото. Несколько слов — через пробел." };
    private readonly ComboBox type = new() { MinWidth = 190, Margin = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox sort = new() { MinWidth = 190, Margin = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center };
    private bool ready;

    public void CaptureDocumentation()
    {
        var folder = Path.Combine(store.Root, "captures");
        Directory.CreateDirectory(folder);
        void Capture(Window window, string file)
        {
            window.UpdateLayout();
            var visual = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
                context.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
            }
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, file + ".png"));
            encoder.Save(stream);
        }
        void CaptureNextModal(string file)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var modal = OwnedWindows.Cast<Window>().Single();
                Capture(modal, file);
                modal.Close();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        Capture(this, "01-guest");
        store.Login("ivanov_ps", "user123");
        Refresh();
        UpdateLayout();
        Capture(this, "02-customer");
        store.AddToCart(store.Products()[0]);
        CaptureNextModal("03-cart");
        Cart();
        store.Login("admin", "admin123");
        Refresh();
        UpdateLayout();
        Capture(this, "04-admin");
        CaptureNextModal("05-editor");
        Edit(store.Products()[0]);
        var orders = new OrdersWindow(store) { Owner = this };
        orders.ContentRendered += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            Capture(orders, "06-orders");
            orders.Close();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        orders.ShowDialog();
        store.Logout();
    }

    public MainWindow(Store store)
    {
        this.store = store;
        Ui.Setup(this, "Каталог продукции");
        Content = layout;
        type.Items.Add("Все типы продукции");
        foreach (var item in store.Db.Run("SELECT Name FROM ProductTypes ORDER BY Name"))
            type.Items.Add(item[0]);
        type.SelectedIndex = 0;
        sort.Items.Add("По умолчанию");
        sort.Items.Add("Цена по возрастанию");
        sort.Items.Add("Цена по убыванию");
        sort.SelectedIndex = 0;
        search.TextChanged += (_, _) => RefreshRows();
        type.SelectionChanged += (_, _) => RefreshRows();
        sort.SelectionChanged += (_, _) => RefreshRows();
        ready = true;
        Refresh();
    }

    private void Refresh()
    {
        layout.Children.Clear();
        var header = new DockPanel { Background = Ui.Secondary, Margin = new Thickness(0, 0, 0, 16), LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        var identity = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(15) };
        DockPanel.SetDock(identity, Dock.Right);
        identity.Children.Add(Ui.Text(store.CurrentUser?.FullName ?? "Гость"));
        identity.Children.Add(Ui.Text(store.CurrentUser?.Role ?? "Просмотр каталога", 13));
        identity.Children.Add(Ui.Button(store.CurrentUser == null ? "Войти" : "Выйти", () =>
        {
            if (store.CurrentUser == null)
                Login();
            else
                store.Logout();
            Refresh();
        }));
        header.Children.Add(identity);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        brand.Children.Add(new Image { Source = Ui.LoadImage(Path.Combine(store.Root, "Import/import/IconVe.png")), Width = 80, Height = 80, Stretch = Stretch.Uniform });
        var words = new StackPanel { Margin = new Thickness(15, 5, 0, 0) };
        words.Children.Add(Ui.Text("ОАО «Жар-птица»", 29));
        words.Children.Add(Ui.Text("Лекарства с доставкой", 17));
        brand.Children.Add(words);
        header.Children.Add(brand);
        layout.Children.Add(header);

        var toolbar = new StackPanel { Margin = new Thickness(18, 0, 18, 8) };
        DockPanel.SetDock(toolbar, Dock.Top);
        toolbar.Children.Add(Ui.Text("Каталог продукции", 26));
        var navigation = new WrapPanel();
        if (store.CurrentUser != null)
            navigation.Children.Add(Ui.Button("Корзина", () => { Cart(); RefreshRows(); }));
        if (store.CanManage)
        {
            navigation.Children.Add(Ui.Button("Добавить продукцию", () => { Edit(null); RefreshRows(); }));
            navigation.Children.Add(Ui.Button("Заказы", () => new OrdersWindow(store) { Owner = this }.ShowDialog()));
        }
        toolbar.Children.Add(navigation);
        var filters = new WrapPanel();
        // Controls are reused to retain the search, filter and sort after edits.
        if (search.Parent is Panel previousSearch)
            previousSearch.Children.Remove(search);
        if (type.Parent is Panel previousType)
            previousType.Children.Remove(type);
        if (sort.Parent is Panel previousSort)
            previousSort.Children.Remove(sort);
        filters.Children.Add(search);
        filters.Children.Add(type);
        filters.Children.Add(sort);
        toolbar.Children.Add(Ui.Text("Поиск по всем текстовым данным · Тип продукции · Сортировка цены", 13));
        toolbar.Children.Add(filters);
        layout.Children.Add(toolbar);
        if (count.Parent is Panel previousCount)
            previousCount.Children.Remove(count);
        DockPanel.SetDock(count, Dock.Bottom);
        count.Margin = new Thickness(24, 8, 24, 12);
        layout.Children.Add(count);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18, 0, 18, 0) };
        if (rows.Parent is ScrollViewer oldScroll)
            oldScroll.Content = null;
        scroll.Content = rows;
        layout.Children.Add(scroll);
        RefreshRows();
    }

    private void RefreshRows()
    {
        if (!ready)
            return;
        Ui.Guard(() =>
        {
            var all = store.Products();
            IEnumerable<Product> products = all;
            var tokens = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            products = products.Where(p => tokens.All(token => string.Join(" ", p.Name, p.Type, p.Description, p.Composition, p.ImagePath).Contains(token, StringComparison.CurrentCultureIgnoreCase)));
            if (type.SelectedIndex > 0)
                products = products.Where(p => p.Type == (string)type.SelectedItem);
            products = sort.SelectedIndex switch
            {
                1 => products.OrderBy(p => p.SaleCents).ThenBy(p => p.Id),
                2 => products.OrderByDescending(p => p.SaleCents).ThenBy(p => p.Id),
                _ => products.OrderBy(p => p.Id)
            };
            rows.Children.Clear();
            var visible = products.ToList();
            foreach (var product in visible)
                rows.Children.Add(ProductCard(product, false));
            if (visible.Count == 0)
                rows.Children.Add(Ui.Text("Ничего не найдено. Измените поисковый запрос или тип продукции.", 20));
            count.Text = $"Показано {visible.Count} из {all.Count}   ·   В корзине: {store.Cart.Values.Sum()} шт.";
        });
    }

    private Border ProductCard(Product product, bool inCart)
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.Children.Add(Ui.ProductImage(store, product.ImagePath));
        var info = new StackPanel();
        Grid.SetColumn(info, 1);
        info.Children.Add(Ui.Text(product.Type.ToUpperInvariant(), 12));
        var title = Ui.Button(product.Name, () => { if (store.CanManage && !inCart) Edit(product); else Details(product); RefreshRows(); });
        title.HorizontalAlignment = HorizontalAlignment.Left;
        info.Children.Add(title);
        info.Children.Add(Ui.Text("Состав: " + product.Composition, 14));
        info.Children.Add(Ui.Text(product.Description, 14));
        grid.Children.Add(info);
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 2);
        actions.Children.Add(Ui.Text(product.PriceText, 24));
        if (product.DiscountActive && product.Discount > 0)
            actions.Children.Add(Ui.Text($"Скидка {product.Discount}% · Было {product.PriceCents / 100m:N2} ₽", 13));
        actions.Children.Add(Ui.Text($"В наличии: {product.Stock} шт.", 13));
        if (inCart)
            actions.Children.Add(Ui.Text($"В корзине: {store.Cart[product.Id]} шт.", 14));
        else if (store.CurrentUser != null)
            actions.Children.Add(Ui.Button("В корзину", () => { store.AddToCart(product); RefreshRows(); }));
        else
            actions.Children.Add(Ui.Text("Войдите для заказа", 13));
        if (store.CanManage && !inCart)
            actions.Children.Add(Ui.Button("Удалить", () =>
            {
                if (!Ui.Confirm($"Удалить продукцию «{product.Name}»? Отменить удаление будет невозможно."))
                    return;
                store.DeleteProduct(product);
                DeleteOldImage(product.ImagePath);
                RefreshRows();
            }));
        grid.Children.Add(actions);
        return new Border { Child = grid, Background = product.DiscountActive && product.Discount > 10 ? Ui.Discount : Brushes.White, BorderBrush = Ui.Secondary, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(4, 4, 4, 10) };
    }

    private void Login()
    {
        var window = new Window { Owner = this };
        Ui.Setup(window, "Авторизация", 760, 550);
        var panel = new StackPanel { Margin = new Thickness(70, 30, 70, 20) };
        window.Content = panel;
        panel.Children.Add(Ui.Text("Вход в систему", 28));
        var login = Ui.Field(panel, "Логин");
        panel.Children.Add(Ui.Text("Пароль"));
        var password = new PasswordBox { Padding = new Thickness(9), Margin = new Thickness(4) };
        panel.Children.Add(password);
        panel.Children.Add(Ui.Button("Войти", () =>
        {
            if (!store.Login(login.Text, password.Password))
                throw new InvalidOperationException("Неверный логин или пароль. Проверьте раскладку клавиатуры и введённые данные.");
            window.DialogResult = true;
        }));
        panel.Children.Add(Ui.Button("Назад", window.Close));
        window.ShowDialog();
    }

    private void Details(Product product)
    {
        var window = new Window { Owner = this };
        Ui.Setup(window, "Информация о продукции", 760, 650);
        var panel = new StackPanel { Margin = new Thickness(24) };
        window.Content = new ScrollViewer { Content = panel };
        panel.Children.Add(Ui.Text(product.Name, 28));
        panel.Children.Add(Ui.ProductImage(store, product.ImagePath, 300, 200));
        panel.Children.Add(Ui.Text(product.Type));
        panel.Children.Add(Ui.Text(product.Description));
        panel.Children.Add(Ui.Text("Состав: " + product.Composition));
        panel.Children.Add(Ui.Text(product.PriceText, 24));
        if (store.CurrentUser != null)
            panel.Children.Add(Ui.Button("Добавить в корзину", () => { store.AddToCart(product); MessageBox.Show("Товар добавлен в корзину.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information); }));
        panel.Children.Add(Ui.Button("Назад", window.Close));
        window.ShowDialog();
    }

    private void Cart()
    {
        var window = new Window { Owner = this };
        Ui.Setup(window, "Корзина");
        var root = new DockPanel { Margin = new Thickness(18) };
        var heading = Ui.Text("Корзина", 26);
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);
        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        var list = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = root;
        var total = Ui.Text("", 23);
        footer.Children.Add(total);
        var address = Ui.Field(footer, "Адрес доставки");
        void Update()
        {
            list.Children.Clear();
            var products = store.Products().Where(p => store.Cart.ContainsKey(p.Id)).ToList();
            foreach (var product in products)
            {
                list.Children.Add(ProductCard(product, true));
                list.Children.Add(Ui.Button("Убрать одну единицу: " + product.Name, () =>
                {
                    if (--store.Cart[product.Id] == 0)
                        store.Cart.Remove(product.Id);
                    Update();
                }));
            }
            total.Text = "Итого: " + (products.Sum(p => (long)p.SaleCents * store.Cart[p.Id]) / 100m).ToString("N2") + " ₽";
            if (products.Count == 0)
                list.Children.Add(Ui.Text("Корзина пуста. Вернитесь в каталог и добавьте товары.", 22));
        }
        footer.Children.Add(Ui.Button("Оформить весь заказ", () =>
        {
            var id = store.Checkout(address.Text);
            MessageBox.Show($"Заказ №{id} оформлен. Корзина очищена.", "Заказ оформлен", MessageBoxButton.OK, MessageBoxImage.Information);
            window.Close();
        }));
        footer.Children.Add(Ui.Button("Назад в каталог", window.Close));
        Update();
        window.ShowDialog();
    }

    private void DeleteOldImage(string path)
    {
        // Shared resources must survive replacement in another product.
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..") || Path.GetFileName(path) is "picture.png" or "IconVe.png" || store.Db.Scalar("SELECT COUNT(*) FROM Products WHERE ImagePath=?", path) != 0)
            return;
        try
        {
            File.Delete(Path.Combine(store.Root, path));
        }
        catch (IOException)
        {
            MessageBox.Show("Данные сохранены. Старое фото занято другой программой; удалите его позже: " + path, "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Edit(Product? product)
    {
        Store.Require(store.CanManage);
        // Modal ownership prevents a second editor and concurrent edits from the catalogue.
        var window = new Window { Owner = this };
        Ui.Setup(window, product == null ? "Добавление продукции" : "Редактирование продукции", 800, 850);
        var panel = new StackPanel { Margin = new Thickness(24) };
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(Ui.Text(product == null ? "Новая продукция" : "Редактирование продукции", 26));
        if (product != null)
        {
            var id = Ui.Field(panel, "ID (только чтение)", product.Id.ToString());
            id.IsReadOnly = true;
        }
        var selectedPhoto = "";
        var imagePath = product?.ImagePath ?? "";
        var photo = Ui.ProductImage(store, imagePath, 300, 200);
        panel.Children.Add(photo);
        panel.Children.Add(Ui.Button("Выбрать фото (до 300 × 200)", () =>
        {
            var picker = new OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp" };
            if (picker.ShowDialog(window) != true)
                return;
            var bitmap = Ui.LoadImage(picker.FileName);
            if (bitmap.PixelWidth > 300 || bitmap.PixelHeight > 200)
                throw new InvalidOperationException("Размер фото превышает 300 × 200 пикселей. Уменьшите изображение и выберите его снова.");
            selectedPhoto = picker.FileName;
            photo.Source = bitmap;
        }));
        var name = Ui.Field(panel, "Наименование", product?.Name ?? "");
        panel.Children.Add(Ui.Text("Тип продукции"));
        var types = store.Db.Run("SELECT Id,Name FROM ProductTypes ORDER BY Name");
        var category = new ComboBox { ItemsSource = types.Select(t => t[1]).ToList(), SelectedIndex = product == null ? 0 : types.FindIndex(t => int.Parse(t[0]) == product.TypeId), Padding = new Thickness(8), Margin = new Thickness(4) };
        panel.Children.Add(category);
        var price = Ui.Field(panel, "Стоимость, ₽", (product?.PriceCents / 100m ?? 0).ToString("F2"));
        var description = Ui.Field(panel, "Описание", product?.Description ?? "", true);
        var composition = Ui.Field(panel, "Состав", product?.Composition ?? "", true);
        var discount = Ui.Field(panel, "Скидка, % (0–100)", (product?.Discount ?? 0).ToString());
        var active = new CheckBox { Content = "Скидка действует", IsChecked = product?.DiscountActive ?? false, Margin = new Thickness(4, 8, 4, 8) };
        panel.Children.Add(active);
        var stock = Ui.Field(panel, "Количество на складе", (product?.Stock ?? 0).ToString());
        var minimum = Ui.Field(panel, "Минимальный остаток", (product?.MinStock ?? 0).ToString());
        panel.Children.Add(Ui.Button("Сохранить", () =>
        {
            if (!decimal.TryParse(price.Text.Replace('.', ','), NumberStyles.Number, CultureInfo.GetCultureInfo("ru-RU"), out var amount) || amount < 0 || amount > 10000000 || decimal.Round(amount, 2) != amount)
                throw new InvalidOperationException("Введите цену от 0 до 10 000 000 рублей, не более двух знаков после запятой.");
            if (!int.TryParse(discount.Text, out var discountValue) || discountValue is < 0 or > 100 || !int.TryParse(stock.Text, out var stockValue) || stockValue < 0 || !int.TryParse(minimum.Text, out var minimumValue) || minimumValue < 0)
                throw new InvalidOperationException("Скидка — целое число от 0 до 100. Остатки — целые неотрицательные числа.");
            if (string.IsNullOrWhiteSpace(name.Text) || category.SelectedIndex < 0)
                throw new InvalidOperationException("Заполните наименование и выберите тип продукции.");
            var newPath = imagePath;
            if (selectedPhoto != "")
            {
                Directory.CreateDirectory(Path.Combine(store.Root, "Images"));
                newPath = "Images/" + Guid.NewGuid().ToString("N") + ".png";
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)photo.Source));
                using var output = File.Create(Path.Combine(store.Root, newPath));
                encoder.Save(output);
            }
            try
            {
                store.SaveProduct(new Product(product?.Id ?? 0, name.Text.Trim(), int.Parse(types[category.SelectedIndex][0]), category.Text, description.Text.Trim(), composition.Text.Trim(), (int)(amount * 100), discountValue, active.IsChecked == true, minimumValue, stockValue, newPath));
            }
            catch
            {
                if (newPath != imagePath)
                    File.Delete(Path.Combine(store.Root, newPath));
                throw;
            }
            if (newPath != imagePath)
                DeleteOldImage(imagePath);
            window.DialogResult = true;
        }));
        panel.Children.Add(Ui.Button("Назад без сохранения", window.Close));
        window.ShowDialog();
    }
}
