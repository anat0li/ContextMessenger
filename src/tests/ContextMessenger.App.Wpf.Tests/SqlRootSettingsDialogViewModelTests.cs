using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.App.Wpf.ViewModels;
using ContextMessenger.Core.Meta;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class SqlRootSettingsDialogViewModelTests
{
    [Fact]
    public void Initializes_from_selected_sql_root_and_keeps_name_read_only()
    {
        var root = SqlRoot("Database", "literal:Data Source=test.db") with
        {
            Description = "Test database",
            Sql = SqlRoot("Database", "literal:Data Source=test.db").Sql! with
            {
                ReadOnly = false,
                MaxRows = 42,
            },
        };

        var viewModel = new SqlRootSettingsDialogViewModel([root], root, new TestSqlConnectionTester());

        Assert.True(viewModel.IsExistingRoot);
        Assert.False(viewModel.IsNewRoot);
        Assert.Equal("Database", viewModel.RootName);
        Assert.Equal("Test database", viewModel.Description);
        Assert.True(viewModel.IsLiteralConnectionString);
        Assert.Equal("Data Source=test.db", viewModel.ConnectionStringValue);
        Assert.Equal(42, viewModel.MaxRows);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public void Provider_choices_include_supported_well_known_providers()
    {
        var viewModel = new SqlRootSettingsDialogViewModel([], null, new TestSqlConnectionTester());

        Assert.Contains("Microsoft.Data.Sqlite", viewModel.ProviderChoices);
        Assert.Contains("Microsoft.Data.SqlClient", viewModel.ProviderChoices);
        Assert.Contains("MySql.Data", viewModel.ProviderChoices);
    }

    [Fact]
    public void New_root_choice_is_visually_special()
    {
        var viewModel = new SqlRootSettingsDialogViewModel([], null, new TestSqlConnectionTester());

        Assert.Equal("*** New SQL root ***", viewModel.RootChoices.First().DisplayName);
    }

    [Fact]
    public void Initializes_new_root_defaults_when_selected_root_is_not_sql()
    {
        var repository = new RootProfile { Name = "Repository", Path = "C:/repo" };

        var viewModel = new SqlRootSettingsDialogViewModel([repository], repository, new TestSqlConnectionTester());

        Assert.True(viewModel.IsNewRoot);
        Assert.Equal("", viewModel.RootName);
        Assert.Equal("", viewModel.ProviderInvariantName);
        Assert.True(viewModel.IsEnvironmentConnectionString);
        Assert.Equal("", viewModel.ConnectionStringValue);
        Assert.Equal(32, viewModel.MaxCellBytes);
        Assert.False(viewModel.SelectCommand.CanExecute(null));
    }

    [Fact]
    public void Test_rejects_duplicate_new_root_name()
    {
        var existing = SqlRoot("Database", "literal:Data Source=test.db");
        var viewModel = new SqlRootSettingsDialogViewModel([existing], null, new TestSqlConnectionTester())
        {
            RootName = "Database",
            ProviderInvariantName = "Microsoft.Data.Sqlite",
            IsLiteralConnectionString = true,
            ConnectionStringValue = "Data Source=other.db",
        };

        viewModel.TestCommand.Execute(null);

        Assert.Null(viewModel.Result);
        Assert.Equal("Root name 'Database' already exists.", viewModel.ValidationMessage);
        Assert.False(viewModel.SelectCommand.CanExecute(null));
    }

    [Fact]
    public void Select_forces_read_only_true()
    {
        var root = SqlRoot("Database", "literal:Data Source=test.db") with
        {
            Sql = SqlRoot("Database", "literal:Data Source=test.db").Sql! with { ReadOnly = false },
        };
        var viewModel = new SqlRootSettingsDialogViewModel([root], root, new TestSqlConnectionTester())
        {
            MaxRows = 25,
        };

        viewModel.SelectCommand.Execute(null);

        Assert.NotNull(viewModel.Result);
        var result = viewModel.Result;
        Assert.True(result.Root.Sql?.ReadOnly);
        Assert.Equal(25, result.Root.Sql?.MaxRows);
    }

    [Fact]
    public void Test_uses_current_form_values_without_selecting()
    {
        var tester = new TestSqlConnectionTester();
        var viewModel = new SqlRootSettingsDialogViewModel([], null, tester)
        {
            RootName = "Reporting",
            ProviderInvariantName = "Microsoft.Data.Sqlite",
            IsLiteralConnectionString = true,
            ConnectionStringValue = "Data Source=reporting.db",
            MaxRows = 7,
        };

        viewModel.TestCommand.Execute(null);

        Assert.Null(viewModel.Result);
        Assert.Equal("Reporting", tester.Root?.Name);
        Assert.Equal("literal:Data Source=reporting.db", tester.Root?.Sql?.ConnectionStringRef);
        Assert.Equal(7, tester.Root?.Sql?.MaxRows);
        Assert.True(tester.Root?.Sql?.ReadOnly);
        Assert.Equal("SQL connection succeeded for Reporting.", viewModel.TestStatus);
        Assert.True(viewModel.SelectCommand.CanExecute(null));
    }

    [Fact]
    public void Editable_provider_value_is_used_even_when_not_in_provider_choices()
    {
        var tester = new TestSqlConnectionTester();
        var viewModel = new SqlRootSettingsDialogViewModel([], null, tester)
        {
            RootName = "Reporting",
            ProviderInvariantName = "Custom.Provider",
            ConnectionStringValue = "CONTEXT_REPORTING",
        };

        viewModel.TestCommand.Execute(null);

        Assert.Equal("Custom.Provider", tester.Root?.Sql?.ProviderInvariantName);
    }

    [Fact]
    public void New_root_select_is_disabled_until_successful_test_and_resets_after_change()
    {
        var viewModel = new SqlRootSettingsDialogViewModel([], null, new TestSqlConnectionTester())
        {
            RootName = "Reporting",
            ProviderInvariantName = "Microsoft.Data.Sqlite",
            ConnectionStringValue = "CONTEXT_REPORTING",
        };

        Assert.False(viewModel.SelectCommand.CanExecute(null));

        viewModel.TestCommand.Execute(null);

        Assert.True(viewModel.SelectCommand.CanExecute(null));

        viewModel.MaxRows = 200;

        Assert.False(viewModel.SelectCommand.CanExecute(null));
    }

    [Fact]
    public void Select_after_successful_new_root_test_returns_root()
    {
        var viewModel = new SqlRootSettingsDialogViewModel([], null, new TestSqlConnectionTester())
        {
            RootName = "Reporting",
            ProviderInvariantName = "Microsoft.Data.Sqlite",
            ConnectionStringValue = "CONTEXT_REPORTING",
        };

        viewModel.TestCommand.Execute(null);
        viewModel.SelectCommand.Execute(null);

        Assert.NotNull(viewModel.Result);
        var result = viewModel.Result;
        Assert.True(result.IsNewRoot);
        Assert.Equal("Reporting", result.Root.Name);
        Assert.Equal("env:CONTEXT_REPORTING", result.Root.Sql?.ConnectionStringRef);
    }

    [Fact]
    public void Switching_roots_with_dirty_form_can_be_declined()
    {
        var first = SqlRoot("First", "literal:Data Source=first.db");
        var second = SqlRoot("Second", "literal:Data Source=second.db");
        var viewModel = new SqlRootSettingsDialogViewModel(
            [first, second],
            first,
            new TestSqlConnectionTester(),
            _ => false);

        viewModel.ConnectionStringValue = "Data Source=changed.db";
        var secondChoice = viewModel.RootChoices.Single(choice => choice.Root?.Name == "Second");

        viewModel.SelectedChoice = secondChoice;

        Assert.Equal("First", viewModel.SelectedChoice?.Root?.Name);
        Assert.Equal("Data Source=changed.db", viewModel.ConnectionStringValue);
    }

    [Fact]
    public void Switching_roots_with_dirty_form_defers_declined_selection_revert()
    {
        var pending = new Queue<Action>();
        var first = SqlRoot("First", "literal:Data Source=first.db");
        var second = SqlRoot("Second", "literal:Data Source=second.db");
        var viewModel = new SqlRootSettingsDialogViewModel(
            [first, second],
            first,
            new TestSqlConnectionTester(),
            _ => false,
            pending.Enqueue);

        viewModel.ConnectionStringValue = "Data Source=changed.db";
        var secondChoice = viewModel.RootChoices.Single(choice => choice.Root?.Name == "Second");

        viewModel.SelectedChoice = secondChoice;

        Assert.Equal("Second", viewModel.SelectedChoice?.Root?.Name);

        pending.Dequeue().Invoke();

        Assert.Equal("First", viewModel.SelectedChoice?.Root?.Name);
        Assert.Equal("Data Source=changed.db", viewModel.ConnectionStringValue);
    }

    [Fact]
    public void Switching_roots_with_dirty_form_can_be_confirmed()
    {
        var first = SqlRoot("First", "literal:Data Source=first.db");
        var second = SqlRoot("Second", "literal:Data Source=second.db");
        var viewModel = new SqlRootSettingsDialogViewModel(
            [first, second],
            first,
            new TestSqlConnectionTester(),
            _ => true);

        viewModel.ConnectionStringValue = "Data Source=changed.db";
        var secondChoice = viewModel.RootChoices.Single(choice => choice.Root?.Name == "Second");

        viewModel.SelectedChoice = secondChoice;

        Assert.Equal("Second", viewModel.SelectedChoice?.Root?.Name);
        Assert.Equal("Data Source=second.db", viewModel.ConnectionStringValue);
    }

    private static RootProfile SqlRoot(string name, string connectionStringRef) => new()
    {
        Name = name,
        Kind = RootKind.Sql,
        Sql = new SqlRootSettings
        {
            ProviderInvariantName = "Microsoft.Data.Sqlite",
            ConnectionStringRef = connectionStringRef,
            ReadOnly = true,
            CommandTimeoutSeconds = 30,
            MaxRows = 100,
            MaxCellBytes = 32,
            AllowSchemaCommands = true,
        },
    };

    private sealed class TestSqlConnectionTester : ISqlConnectionTester
    {
        public RootProfile? Root { get; private set; }

        public void Test(RootProfile root) => Root = root;
    }
}
