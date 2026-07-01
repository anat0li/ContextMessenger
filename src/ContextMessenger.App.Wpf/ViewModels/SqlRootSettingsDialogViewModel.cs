using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContextMessenger.App.Wpf.Services;
using ContextMessenger.App.Wpf.Settings;
using ContextMessenger.Core.Meta;

namespace ContextMessenger.App.Wpf.ViewModels;

public sealed partial class SqlRootSettingsDialogViewModel : ObservableObject
{
    private const int DefaultMaxCellBytes = 32;
    private static readonly string[] WellKnownProviders =
    [
        "Microsoft.Data.Sqlite",
        "Microsoft.Data.SqlClient",
        "MySql.Data",
    ];

    private readonly IReadOnlyList<RootProfile> _allRoots;
    private readonly ISqlConnectionTester _connectionTester;
    private readonly Func<string, bool> _confirmDiscardChanges;
    private readonly Action<Action> _postSelectionRevert;
    private readonly SqlRootSelectionItem _newRootItem;
    private RootProfile? _originalRoot;
    private RootProfile? _lastSuccessfulTestRoot;
    private SqlRootSelectionItem? _previousChoice;
    private bool _isLoading;
    private bool _isRevertingSelection;

    public SqlRootSettingsDialogViewModel(
        IEnumerable<RootProfile> roots,
        RootProfile? selectedRoot,
        ISqlConnectionTester connectionTester,
        Func<string, bool>? confirmDiscardChanges = null,
        Action<Action>? postSelectionRevert = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        _allRoots = roots.ToArray();
        _connectionTester = connectionTester ?? throw new ArgumentNullException(nameof(connectionTester));
        _confirmDiscardChanges = confirmDiscardChanges ?? (_ => true);
        _postSelectionRevert = postSelectionRevert ?? (action => action());
        _newRootItem = SqlRootSelectionItem.NewRoot();

        RootChoices = new ObservableCollection<SqlRootSelectionItem>(
            _allRoots
                .Where(root => root.Kind == RootKind.Sql)
                .OrderBy(root => root.Name, StringComparer.OrdinalIgnoreCase)
                .Select(SqlRootSelectionItem.Existing)
                .Prepend(_newRootItem));

        var initial = selectedRoot?.Kind == RootKind.Sql
            ? RootChoices.FirstOrDefault(item => item.Root is not null &&
                                                 string.Equals(item.Root.Name, selectedRoot.Name, StringComparison.Ordinal))
            : null;

        LoadSelection(initial ?? _newRootItem);
    }

    public ObservableCollection<SqlRootSelectionItem> RootChoices { get; }

    public IReadOnlyList<string> ProviderChoices => WellKnownProviders;

    [ObservableProperty]
    private SqlRootSelectionItem? _selectedChoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExistingRoot))]
    [NotifyPropertyChangedFor(nameof(IsNewRoot))]
    private bool _isEditingExistingRoot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private string _rootName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private string? _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private string _providerInvariantName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private string? _providerAssemblyPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private string? _providerFactoryTypeName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(IsLiteralConnectionString))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private bool _isEnvironmentConnectionString = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private string _connectionStringValue = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private int _commandTimeoutSeconds = 30;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private int _maxRows = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private int _maxCellBytes = DefaultMaxCellBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private bool _allowSchemaCommands = true;

    [ObservableProperty]
    private string _testStatus = "";

    [ObservableProperty]
    private string _validationMessage = "";

    public bool IsExistingRoot => IsEditingExistingRoot;

    public bool IsNewRoot => !IsEditingExistingRoot;

    public bool IsDirty => BuildCandidate(skipValidation: true) != _originalRoot;

    public bool IsLiteralConnectionString
    {
        get => !IsEnvironmentConnectionString;
        set
        {
            if (value)
                IsEnvironmentConnectionString = false;
        }
    }

    public SqlRootDialogResult? Result { get; private set; }

    public event EventHandler? Completed;

    [RelayCommand]
    private void Test()
    {
        if (!TryBuildCandidate(out var root))
            return;

        TestStatus = $"Testing SQL connection for {root.Name}...";
        try
        {
            _connectionTester.Test(root);
            _lastSuccessfulTestRoot = root;
            TestStatus = $"SQL connection succeeded for {root.Name}.";
            SelectCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _lastSuccessfulTestRoot = null;
            TestStatus = $"SQL connection failed for {root.Name}: {ex.Message}";
            SelectCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void Select()
    {
        if (!TryBuildCandidate(out var root))
            return;

        Result = new SqlRootDialogResult(root, IsNewRoot);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSelect() =>
        IsExistingRoot || (_lastSuccessfulTestRoot is not null &&
                           _lastSuccessfulTestRoot == BuildCandidate(skipValidation: true));

    partial void OnSelectedChoiceChanging(SqlRootSelectionItem? oldValue, SqlRootSelectionItem? newValue)
    {
        if (_isLoading || _isRevertingSelection || oldValue is null || Equals(oldValue, newValue))
            return;

        _previousChoice = oldValue;
    }

    partial void OnSelectedChoiceChanged(SqlRootSelectionItem? value)
    {
        if (_isLoading || _isRevertingSelection || value is null)
            return;

        var previous = _previousChoice;
        _previousChoice = null;
        if (previous is not null && IsDirty && !_confirmDiscardChanges("Discard unsaved SQL root changes?"))
        {
            _postSelectionRevert(() =>
            {
                _isRevertingSelection = true;
                try
                {
                    SelectedChoice = previous;
                }
                finally
                {
                    _isRevertingSelection = false;
                }
            });

            return;
        }

        LoadSelection(value);
    }

    private void LoadSelection(SqlRootSelectionItem item)
    {
        _isLoading = true;
        try
        {
            SelectedChoice = item;
            IsEditingExistingRoot = item.Root is not null;
            _originalRoot = item.Root is null
                ? CreateDefaultRoot()
                : NormalizeSqlRoot(item.Root);

            var root = _originalRoot;
            RootName = root.Name;
            Description = root.Description;
            ProviderInvariantName = root.Sql?.ProviderInvariantName ?? "";
            ProviderAssemblyPath = root.Sql?.ProviderAssemblyPath;
            ProviderFactoryTypeName = root.Sql?.ProviderFactoryTypeName;
            SetConnectionStringParts(root.Sql?.ConnectionStringRef ?? "");
            CommandTimeoutSeconds = root.Sql?.CommandTimeoutSeconds ?? 30;
            MaxRows = root.Sql?.MaxRows ?? 100;
            MaxCellBytes = root.Sql?.MaxCellBytes ?? DefaultMaxCellBytes;
            AllowSchemaCommands = root.Sql?.AllowSchemaCommands ?? true;
            _lastSuccessfulTestRoot = null;
            ValidationMessage = "";
            TestStatus = "";
        }
        finally
        {
            _isLoading = false;
        }

        OnPropertyChanged(nameof(IsDirty));
    }

    private bool TryBuildCandidate(out RootProfile root)
    {
        root = BuildCandidate(skipValidation: false);
        return string.IsNullOrEmpty(ValidationMessage);
    }

    private RootProfile BuildCandidate(bool skipValidation)
    {
        var name = IsEditingExistingRoot
            ? _originalRoot?.Name ?? RootName
            : RootName.Trim();

        if (!skipValidation)
        {
            ValidationMessage = Validate(name);
            if (!string.IsNullOrEmpty(ValidationMessage))
                return CreateCandidate(name);
        }

        return CreateCandidate(name);
    }

    private RootProfile CreateCandidate(string name) => new()
    {
        Name = name,
        Kind = RootKind.Sql,
        Description = NormalizeOptional(Description),
        Sql = new SqlRootSettings
        {
            ProviderInvariantName = ProviderInvariantName.Trim(),
            ProviderAssemblyPath = NormalizeOptional(ProviderAssemblyPath),
            ProviderFactoryTypeName = NormalizeOptional(ProviderFactoryTypeName),
            ConnectionStringRef = BuildConnectionStringRef(),
            ReadOnly = true,
            CommandTimeoutSeconds = CommandTimeoutSeconds,
            MaxRows = MaxRows,
            MaxCellBytes = MaxCellBytes,
            AllowSchemaCommands = AllowSchemaCommands,
        },
    };

    private string Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Root name is required.";

        if (IsNewRoot && _allRoots.Any(root => string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"Root name '{name}' already exists.";

        if (string.IsNullOrWhiteSpace(ProviderInvariantName))
            return "Provider invariant name is required.";

        if (string.IsNullOrWhiteSpace(ConnectionStringValue))
            return IsEnvironmentConnectionString
                ? "Connection string environment variable name is required."
                : "Connection string is required.";

        if (CommandTimeoutSeconds <= 0)
            return "Command timeout must be greater than zero.";

        if (MaxRows <= 0)
            return "Max rows must be greater than zero.";

        if (MaxCellBytes <= 0)
            return "Max cell bytes must be greater than zero.";

        return "";
    }

    private RootProfile CreateDefaultRoot()
    {
        return new RootProfile
        {
            Name = "",
            Kind = RootKind.Sql,
            Sql = new SqlRootSettings
            {
                ProviderInvariantName = "",
                ConnectionStringRef = "env:",
                ReadOnly = true,
                CommandTimeoutSeconds = 30,
                MaxRows = 100,
                MaxCellBytes = DefaultMaxCellBytes,
                AllowSchemaCommands = true,
            },
        };
    }

    private static RootProfile NormalizeSqlRoot(RootProfile root) => root with
    {
        Kind = RootKind.Sql,
        Sql = (root.Sql ?? new SqlRootSettings()) with { ReadOnly = true },
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string BuildConnectionStringRef()
    {
        var value = ConnectionStringValue.Trim();
        return IsEnvironmentConnectionString ? $"env:{value}" : $"literal:{value}";
    }

    private void SetConnectionStringParts(string reference)
    {
        const string environmentPrefix = "env:";
        const string literalPrefix = "literal:";

        if (reference.StartsWith(literalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            IsEnvironmentConnectionString = false;
            ConnectionStringValue = reference[literalPrefix.Length..];
            return;
        }

        IsEnvironmentConnectionString = true;
        ConnectionStringValue = reference.StartsWith(environmentPrefix, StringComparison.OrdinalIgnoreCase)
            ? reference[environmentPrefix.Length..]
            : reference;
    }

    partial void OnRootNameChanged(string value) => ResetSuccessfulTestIfEditingNewRoot();

    partial void OnDescriptionChanged(string? value) => ResetSuccessfulTestIfEditingNewRoot();

    partial void OnProviderInvariantNameChanged(string value) => ResetSuccessfulTestIfEditingNewRoot();

    partial void OnProviderAssemblyPathChanged(string? value) => ResetSuccessfulTestIfEditingNewRoot();

    partial void OnProviderFactoryTypeNameChanged(string? value) => ResetSuccessfulTestIfEditingNewRoot();

    partial void OnIsEnvironmentConnectionStringChanged(bool value) => ResetSuccessfulTestIfEditingNewRoot();

    partial void OnConnectionStringValueChanged(string value) => ResetSuccessfulTestIfEditingNewRoot();

    partial void OnCommandTimeoutSecondsChanged(int value) => ResetSuccessfulTestIfEditingNewRoot();

    partial void OnMaxRowsChanged(int value) => ResetSuccessfulTestIfEditingNewRoot();

    partial void OnMaxCellBytesChanged(int value) => ResetSuccessfulTestIfEditingNewRoot();

    partial void OnAllowSchemaCommandsChanged(bool value) => ResetSuccessfulTestIfEditingNewRoot();

    private void ResetSuccessfulTestIfEditingNewRoot()
    {
        if (_isLoading || IsExistingRoot)
            return;

        _lastSuccessfulTestRoot = null;
        SelectCommand.NotifyCanExecuteChanged();
    }
}

public sealed record SqlRootSelectionItem(string DisplayName, RootProfile? Root)
{
    public static SqlRootSelectionItem NewRoot() => new("*** New SQL root ***", null);

    public static SqlRootSelectionItem Existing(RootProfile root) => new(root.Name, root);
}
