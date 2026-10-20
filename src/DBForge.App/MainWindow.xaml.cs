using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.Data.SqlClient;
using DBForge.Domain;

namespace DBForge.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<EntityDefinition> _entities = new();
    private EntityDefinition? _selectedEntity;
    private SqlConnection? _connection;
    private string? _currentProjectPath;
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DBForgeStudio", "connection.json");
    private readonly string _projectPathDefault = "";

    private static readonly string[] SqlTypes =
    {
        "INT", "BIGINT", "TINYINT", "VARCHAR(255)", "NVARCHAR(255)",
        "TEXT", "NVARCHAR(MAX)", "DECIMAL(10,2)", "DATE", "DATETIME",
        "DATETIME2", "BIT", "BOOLEAN", "FLOAT", "DOUBLE", "JSON"
    };

    public MainWindow()
    {
        InitializeComponent();
        EntityList.ItemsSource = _entities;
        TypeColumn.ItemsSource = SqlTypes;
        LoadConnectionSettings();
        UpdateDashboard();
        Closing += (_, _) => { if (_connection is not null) _connection.Dispose(); };
    }

    private SqlConnectionStringBuilder BuildConnectionString(bool includeDatabase = false)
    {
        int port = 1433;
        if (!string.IsNullOrWhiteSpace(PortBox.Text))
        {
            if (!int.TryParse(PortBox.Text.Trim(), out port) || port < 1 || port > 65535)
                throw new InvalidOperationException("Неверный порт");
        }

        string host = string.IsNullOrWhiteSpace(HostBox.Text) ? "localhost" : HostBox.Text.Trim();

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = port == 1433 ? host : $"{host},{port}",
            ConnectTimeout = 5,
            TrustServerCertificate = true
        };

        if (string.IsNullOrWhiteSpace(UserBox.Text))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = UserBox.Text.Trim();
            builder.Password = PasswordBox.Password;
        }

        if (includeDatabase && !string.IsNullOrWhiteSpace(DatabaseCombo.Text))
        {
            builder.InitialCatalog = DatabaseCombo.Text.Trim();
        }

        return builder;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ConnectionDetails.Text = "Проверка соединения…";
            var builder = BuildConnectionString(false);
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();

            await using (var cmd = new SqlCommand("SELECT @@VERSION", conn))
            {
                var versionStr = Convert.ToString(await cmd.ExecuteScalarAsync())?.Split('\n').FirstOrDefault()?.Trim();
                ConnectionDetails.Text = $"Подключение успешно. {versionStr}";
            }

            var dbs = new List<string>();
            await using (var cmd = new SqlCommand("SELECT name FROM sys.databases WHERE state_desc = 'ONLINE' ORDER BY name;", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync()) dbs.Add(reader.GetString(0));
            }

            DatabaseList.ItemsSource = dbs;
            DatabaseCombo.ItemsSource = dbs;
            if (string.IsNullOrWhiteSpace(DatabaseCombo.Text) && dbs.Count > 0)
            {
                DatabaseCombo.Text = dbs[0];
            }

            ConnectionBadge.Text = "● SQL Server подключён";
            ConnectionBadge.Foreground = System.Windows.Media.Brushes.LightGreen;
            _connection?.Dispose();
            _connection = new SqlConnection(builder.ConnectionString);
            await _connection.OpenAsync();
            StatusText.Text = "SQL Server доступен; список баз загружен.";
            SaveConnectionSettings(false);
            UpdateDashboard();
        }
        catch (Exception ex)
        {
            ConnectionBadge.Text = "● Ошибка соединения";
            ConnectionBadge.Foreground = System.Windows.Media.Brushes.Salmon;
            ConnectionDetails.Text = "Не удалось подключиться: " + ex.Message;
            StatusText.Text = "Проверьте сервер, порт, учётную запись и права.";
        }
    }

    private async void DatabaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DatabaseList.SelectedItem is not string database) return;
        DatabaseCombo.Text = database;
        UpdateDashboard();
        await LoadTablesAsync(database);
    }

    private async Task LoadTablesAsync(string database)
    {
        try
        {
            var builder = BuildConnectionString(false);
            builder.InitialCatalog = database;

            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME", conn);

            var names = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));

            TableList.ItemsSource = names;
            DataTableCombo.ItemsSource = names;
            DataTableCombo.Text = names.FirstOrDefault() ?? "";
            StatusText.Text = $"База {database}: таблиц найдено {names.Count}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось загрузить таблицы: " + ex.Message;
        }
    }

    private async void TableList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TableList.SelectedItem is not string table || string.IsNullOrWhiteSpace(DatabaseCombo.Text)) return;
        try
        {
            await using var conn = new SqlConnection(BuildConnectionString(true).ConnectionString);
            await conn.OpenAsync();

            const string sql = @"
                SELECT 
                    c.COLUMN_NAME,
                    c.DATA_TYPE + CASE 
                        WHEN c.CHARACTER_MAXIMUM_LENGTH IS NOT NULL AND c.CHARACTER_MAXIMUM_LENGTH > 0 THEN '(' + CAST(c.CHARACTER_MAXIMUM_LENGTH AS VARCHAR(10)) + ')'
                        WHEN c.CHARACTER_MAXIMUM_LENGTH = -1 THEN '(MAX)'
                        ELSE '' END AS COLUMN_TYPE,
                    c.IS_NULLABLE,
                    CASE WHEN EXISTS (
                        SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                        JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu 
                          ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                        WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_NAME = c.TABLE_NAME AND kcu.COLUMN_NAME = c.COLUMN_NAME
                    ) THEN 'PRI' ELSE '' END AS COLUMN_KEY,
                    c.COLUMN_DEFAULT,
                    CASE WHEN COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') = 1 
                         THEN 'IDENTITY(1,1)' ELSE '' END AS EXTRA
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_NAME = @table
                ORDER BY c.ORDINAL_POSITION";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@table", table);

            var dt = new DataTable();
            dt.Columns.Add("Поле");
            dt.Columns.Add("Тип");
            dt.Columns.Add("NULL");
            dt.Columns.Add("Ключ");
            dt.Columns.Add("По умолчанию");
            dt.Columns.Add("Дополнительно");

            await using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                dt.Rows.Add(
                    rd.GetValue(0),
                    rd.GetValue(1),
                    rd.GetValue(2),
                    rd.GetValue(3),
                    rd.IsDBNull(4) ? "" : rd.GetValue(4),
                    rd.GetValue(5)
                );
            }

            DataGridView.ItemsSource = dt.DefaultView;
            DataTableCombo.Text = table;
            StatusText.Text = $"Структура таблицы {table} загружена.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка чтения структуры: " + ex.Message;
        }
    }

    private void AddEntity_Click(object sender, RoutedEventArgs e)
    {
        string name = EntityNameBox.Text.Trim();
        if (!IsIdentifier(name))
        {
            MessageBox.Show("Имя таблицы должно начинаться с буквы или _, содержать только латинские буквы, цифры и _.");
            return;
        }
        if (_entities.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("Сущность с таким именем уже существует.");
            return;
        }
        var entity = new EntityDefinition
        {
            Name = name,
            DisplayName = name,
            Fields = new List<FieldDefinition>
            {
                new() { Name = "id", DataType = "INT", IsPrimaryKey = true, IsRequired = true, IsAutoIncrement = true }
            }
        };
        _entities.Add(entity);
        EntityList.SelectedItem = entity;
        EntityNameBox.Clear();
        UpdateDashboard();
        StatusText.Text = $"Добавлена сущность {name}.";
    }

    private void DeleteEntity_Click(object sender, RoutedEventArgs e)
    {
        if (EntityList.SelectedItem is not EntityDefinition entity) return;
        if (MessageBox.Show($"Удалить сущность «{entity.Name}»?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _entities.Remove(entity);
        _selectedEntity = null;
        FieldsGrid.ItemsSource = null;
        UpdateDashboard();
    }

    private void EntityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedEntity is not null) FieldsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _selectedEntity = EntityList.SelectedItem as EntityDefinition;
        FieldsGrid.ItemsSource = _selectedEntity?.Fields;
    }

    private void AddField_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntity is null)
        {
            MessageBox.Show("Сначала выберите сущность.");
            return;
        }
        _selectedEntity.Fields.Add(new FieldDefinition { Name = "new_field", DataType = "VARCHAR(255)" });
        FieldsGrid.Items.Refresh();
    }

    private void ValidateModel_Click(object sender, RoutedEventArgs e)
    {
        FieldsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FieldsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var errors = ValidateModel();
        ValidationOutput.Text = errors.Count == 0
            ? "Проверка пройдена. Модель не содержит обнаруженных ошибок."
            : string.Join(Environment.NewLine, errors.Select((x, i) => $"{i + 1}. {x}"));
        StatusText.Text = errors.Count == 0 ? "Модель корректна." : $"Найдено ошибок: {errors.Count}.";
        if (errors.Count > 0) MessageBox.Show(ValidationOutput.Text, "Проверка модели", MessageBoxButton.OK, MessageBoxImage.Warning);
        else MessageBox.Show(ValidationOutput.Text, "Проверка модели", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private List<string> ValidateModel()
    {
        var errors = new List<string>();
        if (_entities.Count == 0) errors.Add("Добавьте хотя бы одну сущность.");
        var entityNames = _entities.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in _entities)
        {
            if (!IsIdentifier(entity.Name)) errors.Add($"Сущность «{entity.Name}»: недопустимое имя таблицы.");
            if (entity.Fields.Count == 0) errors.Add($"Сущность «{entity.Name}»: нет полей.");
            var duplicateFields = entity.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Name)).GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (var duplicate in duplicateFields) errors.Add($"Сущность «{entity.Name}»: поле «{duplicate}» повторяется.");
            foreach (var field in entity.Fields)
            {
                if (!IsIdentifier(field.Name)) errors.Add($"«{entity.Name}.{field.Name}»: недопустимое имя поля.");
                if (!SqlTypes.Contains(field.DataType, StringComparer.OrdinalIgnoreCase)) errors.Add($"«{entity.Name}.{field.Name}»: тип данных не поддерживается генератором.");
                if (field.IsAutoIncrement && !new[] { "INT", "BIGINT", "TINYINT" }.Contains(field.DataType, StringComparer.OrdinalIgnoreCase)) errors.Add($"«{entity.Name}.{field.Name}»: IDENTITY разрешён только для целочисленного типа.");
                if ((!string.IsNullOrWhiteSpace(field.ReferenceEntity) || !string.IsNullOrWhiteSpace(field.ReferenceField)) && (string.IsNullOrWhiteSpace(field.ReferenceEntity) || string.IsNullOrWhiteSpace(field.ReferenceField))) errors.Add($"«{entity.Name}.{field.Name}»: заполните и таблицу, и поле внешнего ключа.");
                if (!string.IsNullOrWhiteSpace(field.ReferenceEntity) && !entityNames.Contains(field.ReferenceEntity)) errors.Add($"«{entity.Name}.{field.Name}»: таблица ссылки «{field.ReferenceEntity}» отсутствует в модели.");
            }
            if (entity.Fields.Count(f => f.IsPrimaryKey) > 1) errors.Add($"Сущность «{entity.Name}»: отмечено несколько первичных ключей; текущий генератор поддерживает один PK на таблицу.");
            foreach (var field in entity.Fields.Where(f => !string.IsNullOrWhiteSpace(f.ReferenceEntity)))
            {
                var target = _entities.FirstOrDefault(x => x.Name.Equals(field.ReferenceEntity, StringComparison.OrdinalIgnoreCase));
                if (target is not null && !target.Fields.Any(x => x.Name.Equals(field.ReferenceField, StringComparison.OrdinalIgnoreCase))) errors.Add($"«{entity.Name}.{field.Name}»: поле ссылки «{field.ReferenceEntity}.{field.ReferenceField}» не найдено.");
            }
        }
        return errors;
    }

    private static string MapToSqlServerType(string type) => type.ToUpperInvariant() switch
    {
        "BOOLEAN" => "BIT",
        "DOUBLE" => "FLOAT",
        "TEXT" => "NVARCHAR(MAX)",
        "JSON" => "NVARCHAR(MAX)",
        "TIMESTAMP" => "DATETIME2",
        _ => type
    };

    private void GenerateSql_Click(object sender, RoutedEventArgs e)
    {
        FieldsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FieldsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var errors = ValidateModel();
        if (errors.Count > 0)
        {
            SqlPreview.Text = "-- Исправьте ошибки модели перед генерацией:\n-- " + string.Join("\n-- ", errors);
            StatusText.Text = "SQL не создан: модель содержит ошибки.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("-- DBForge Studio generated script for Microsoft SQL Server (T-SQL)");
        sb.AppendLine("-- Проверьте имя базы и сделайте резервную копию перед применением.");

        foreach (var entity in _entities)
        {
            sb.AppendLine();
            sb.AppendLine($"IF OBJECT_ID(N'[{entity.Name}]', N'U') IS NULL");
            sb.AppendLine($"CREATE TABLE [{entity.Name}] (");
            var lines = new List<string>();

            foreach (var field in entity.Fields)
            {
                string sqlType = MapToSqlServerType(field.DataType);
                string line = $"  [{field.Name}] {sqlType}";
                if (field.IsAutoIncrement) line += " IDENTITY(1,1)";
                if (field.IsRequired || field.IsPrimaryKey) line += " NOT NULL";
                else line += " NULL";
                lines.Add(line);

                if (field.IsUnique && !field.IsPrimaryKey)
                {
                    lines.Add($"  CONSTRAINT [uq_{entity.Name}_{field.Name}] UNIQUE ([{field.Name}])");
                }
            }

            var pk = entity.Fields.FirstOrDefault(f => f.IsPrimaryKey);
            if (pk is not null)
            {
                lines.Add($"  CONSTRAINT [pk_{entity.Name}] PRIMARY KEY ([{pk.Name}])");
            }

            foreach (var field in entity.Fields.Where(f => !string.IsNullOrWhiteSpace(f.ReferenceEntity) && !string.IsNullOrWhiteSpace(f.ReferenceField)))
            {
                lines.Add($"  CONSTRAINT [fk_{entity.Name}_{field.Name}] FOREIGN KEY ([{field.Name}]) REFERENCES [{field.ReferenceEntity}] ([{field.ReferenceField}])");
            }

            sb.AppendLine(string.Join(",\n", lines));
            sb.AppendLine(");");
        }

        SqlPreview.Text = sb.ToString();
        StatusText.Text = "SQL для MS SQL Server сгенерирован. Проверьте его перед применением.";
    }

    private void ExportSql_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SqlPreview.Text)) GenerateSql_Click(sender, e);
        var dialog = new SaveFileDialog { Filter = "SQL script (*.sql)|*.sql", FileName = "dbforge-schema.sql" };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, SqlPreview.Text, new UTF8Encoding(false));
            StatusText.Text = "SQL сохранён: " + dialog.FileName;
        }
    }

    private async void ApplySql_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DatabaseCombo.Text))
        {
            MessageBox.Show("Выберите целевую базу на вкладке подключения.");
            return;
        }
        if (string.IsNullOrWhiteSpace(SqlPreview.Text))
        {
            MessageBox.Show("Сначала сгенерируйте SQL.");
            return;
        }

        if (SqlPreview.Text.Contains("ENGINE=InnoDB", StringComparison.OrdinalIgnoreCase) ||
            SqlPreview.Text.Contains("FOREIGN_KEY_CHECKS", StringComparison.OrdinalIgnoreCase))
        {
            GenerateSql_Click(sender, e);
        }

        if (MessageBox.Show($"Будет выполнен SQL-скрипт в базе «{DatabaseCombo.Text}» на сервере {HostBox.Text}. Продолжить? Рекомендуется использовать только тестовую базу.", "Подтверждение применения SQL", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await using var conn = new SqlConnection(BuildConnectionString(true).ConnectionString);
            await conn.OpenAsync();
            var statements = Regex.Split(SqlPreview.Text, @";\s*(?:\r?\n|$)").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            foreach (var statement in statements)
            {
                await using var cmd = new SqlCommand(statement, conn);
                await cmd.ExecuteNonQueryAsync();
            }
            StatusText.Text = "SQL применён успешно.";
            MessageBox.Show("Скрипт выполнен успешно.");
            await LoadTablesAsync(DatabaseCombo.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка применения SQL:\n" + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Применение SQL завершилось ошибкой.";
        }
    }

    private async void LoadData_Click(object sender, RoutedEventArgs e)
    {
        string table = DataTableCombo.Text.Trim();
        if (!IsIdentifier(table))
        {
            MessageBox.Show("Укажите имя таблицы из базы данных.");
            return;
        }
        try
        {
            await using var conn = new SqlConnection(BuildConnectionString(true).ConnectionString);
            await conn.OpenAsync();
            var columns = new List<string>();
            await using (var meta = new SqlCommand("SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t ORDER BY ORDINAL_POSITION", conn))
            {
                meta.Parameters.AddWithValue("@t", table);
                await using var rd = await meta.ExecuteReaderAsync();
                while (await rd.ReadAsync()) columns.Add(rd.GetString(0));
            }
            if (columns.Count == 0)
            {
                MessageBox.Show("Таблица не найдена или нет прав на просмотр.");
                return;
            }
            string where = "";
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                where = " WHERE " + string.Join(" OR ", columns.Select(c => $"CAST([{c}] AS NVARCHAR(MAX)) LIKE @q"));
            }
            string sql = $"SELECT TOP 500 * FROM [{table}]{where}";
            await using var cmd = new SqlCommand(sql, conn);
            if (where.Length > 0) cmd.Parameters.AddWithValue("@q", "%" + SearchBox.Text.Trim() + "%");
            var dt = new DataTable();
            await using var reader = await cmd.ExecuteReaderAsync();
            dt.Load(reader);
            DataGridView.ItemsSource = dt.DefaultView;
            StatusText.Text = $"Загружено строк: {dt.Rows.Count} (максимум 500).";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось загрузить данные:\n" + ex.Message);
        }
    }

    private async void InsertRow_Click(object sender, RoutedEventArgs e)
    {
        string table = DataTableCombo.Text.Trim();
        if (!IsIdentifier(table) || string.IsNullOrWhiteSpace(DatabaseCombo.Text))
        {
            MessageBox.Show("Сначала подключитесь и выберите таблицу.");
            return;
        }
        try
        {
            await using var conn = new SqlConnection(BuildConnectionString(true).ConnectionString);
            await conn.OpenAsync();
            var cols = new List<(string Name, string Type, bool Nullable, bool Auto, string? Default)>();

            const string metaSql = @"
                SELECT 
                    c.COLUMN_NAME, 
                    c.DATA_TYPE, 
                    c.IS_NULLABLE, 
                    COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') AS IS_IDENTITY,
                    c.COLUMN_DEFAULT 
                FROM INFORMATION_SCHEMA.COLUMNS c 
                WHERE c.TABLE_NAME = @t 
                ORDER BY c.ORDINAL_POSITION";

            await using (var meta = new SqlCommand(metaSql, conn))
            {
                meta.Parameters.AddWithValue("@t", table);
                await using var rd = await meta.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    bool isAuto = !rd.IsDBNull(3) && Convert.ToInt32(rd.GetValue(3)) == 1;
                    cols.Add((
                        rd.GetString(0),
                        rd.GetString(1),
                        rd.GetString(2) == "YES",
                        isAuto,
                        rd.IsDBNull(4) ? null : Convert.ToString(rd.GetValue(4))
                    ));
                }
            }

            var values = new Dictionary<string, string?>();
            foreach (var c in cols)
            {
                if (c.Auto) continue;
                string prompt = $"Поле: {c.Name}\nТип: {c.Type}\n{(c.Nullable ? "Можно оставить пустым." : "Обязательное поле.")}\nВведите значение (пусто = NULL/значение по умолчанию):";
                string? value = Prompt("Добавление записи", prompt);
                if (value is null) return;
                if (value.Length == 0 && (c.Nullable || c.Default is not null)) continue;
                if (value.Length == 0 && !c.Nullable)
                {
                    MessageBox.Show($"Поле {c.Name} обязательно.");
                    return;
                }
                values[c.Name] = value;
            }

            string insertSql = values.Count == 0
                ? $"INSERT INTO [{table}] DEFAULT VALUES"
                : $"INSERT INTO [{table}] ({string.Join(",", values.Keys.Select(k => $"[{k}]"))}) VALUES ({string.Join(",", values.Keys.Select((_, i) => "@v" + i))})";

            await using var cmd = new SqlCommand(insertSql, conn);
            int ix = 0;
            foreach (var value in values.Values) cmd.Parameters.AddWithValue("@v" + ix++, value);
            await cmd.ExecuteNonQueryAsync();
            StatusText.Text = "Запись добавлена.";
            LoadData_Click(sender, e);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось добавить запись:\n" + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? Prompt(string title, string message)
    {
        var win = new Window { Title = title, Width = 440, Height = 230, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Owner = System.Windows.Application.Current.MainWindow };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var label = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(label, Dock.Top);
        panel.Children.Add(label);
        var input = new TextBox { Padding = new Thickness(7), Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(input, Dock.Top);
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Продолжить", IsDefault = true, Padding = new System.Windows.Thickness(12, 6, 12, 6), Margin = new System.Windows.Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Padding = new System.Windows.Thickness(12, 6, 12, 6) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        ok.Click += (_, _) => win.DialogResult = true;
        win.Content = panel;
        return win.ShowDialog() == true ? input.Text : null;
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (_entities.Count > 0 && MessageBox.Show("Создать новый проект? Несохранённые изменения будут потеряны.", "Новый проект", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _entities.Clear();
        _selectedEntity = null;
        FieldsGrid.ItemsSource = null;
        _currentProjectPath = null;
        UpdateDashboard();
        StatusText.Text = "Создан пустой проект.";
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        FieldsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        FieldsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var dialog = new SaveFileDialog { Filter = "DBForge project (*.dbforge.json)|*.dbforge.json|JSON (*.json)|*.json", FileName = "project.dbforge.json" };
        if (!string.IsNullOrWhiteSpace(_currentProjectPath)) dialog.FileName = _currentProjectPath;
        if (dialog.ShowDialog() != true) return;
        var project = new ProjectDefinition
        {
            Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(dialog.FileName)),
            Server = HostBox.Text.Trim(),
            Port = int.TryParse(PortBox.Text, out var p) ? p : 1433,
            DatabaseName = DatabaseCombo.Text,
            Entities = _entities.ToList()
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
        _currentProjectPath = dialog.FileName;
        UpdateDashboard();
        StatusText.Text = "Проект сохранён: " + dialog.FileName;
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "DBForge project (*.dbforge.json;*.json)|*.dbforge.json;*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var project = JsonSerializer.Deserialize<ProjectDefinition>(File.ReadAllText(dialog.FileName)) ?? throw new InvalidDataException("Файл проекта пустой или имеет неверный формат.");
            _entities.Clear();
            foreach (var entity in project.Entities) _entities.Add(entity);
            _selectedEntity = null;
            FieldsGrid.ItemsSource = null;
            HostBox.Text = project.Server;
            PortBox.Text = (project.Port == 3306 ? 1433 : project.Port).ToString();
            DatabaseCombo.Text = project.DatabaseName ?? "";
            _currentProjectPath = dialog.FileName;
            UpdateDashboard();
            StatusText.Text = "Проект открыт: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось открыть проект:\n" + ex.Message);
        }
    }

    private void SaveConnection_Click(object sender, RoutedEventArgs e) => SaveConnectionSettings(true);

    private void SaveConnectionSettings(bool showMessage)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new
            {
                Host = HostBox.Text.Trim(),
                Port = PortBox.Text.Trim(),
                User = UserBox.Text.Trim(),
                Database = DatabaseCombo.Text
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (showMessage) MessageBox.Show("Параметры подключения сохранены без пароля. Пароль вводится при каждом запуске.");
        }
        catch (Exception ex)
        {
            if (showMessage) MessageBox.Show("Не удалось сохранить параметры: " + ex.Message);
        }
    }

    private void LoadConnectionSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            var root = doc.RootElement;
            if (root.TryGetProperty("Host", out var host)) HostBox.Text = host.GetString() ?? "localhost";
            if (root.TryGetProperty("Port", out var port))
            {
                var p = port.GetString();
                PortBox.Text = (string.IsNullOrWhiteSpace(p) || p == "3306") ? "1433" : p;
            }
            if (root.TryGetProperty("User", out var user))
            {
                var u = user.GetString();
                UserBox.Text = u == "root" ? "" : (u ?? "");
            }
            if (root.TryGetProperty("Database", out var db)) DatabaseCombo.Text = db.GetString() ?? "";
        }
        catch
        {
            /* повреждённые настройки не должны мешать запуску */
        }
    }

    private void UpdateDashboard()
    {
        ProjectNameLabel.Text = string.IsNullOrWhiteSpace(_currentProjectPath)
            ? "Новый проект"
            : Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(_currentProjectPath));
        EntityCountLabel.Text = $"{_entities.Count} сущностей";
        DatabaseLabel.Text = string.IsNullOrWhiteSpace(DatabaseCombo.Text)
            ? "SQL Server / localhost"
            : DatabaseCombo.Text;
    }

    private static bool IsIdentifier(string value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*$");
}