using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using csharp_interop.Documentation.Services;

namespace csharp_interop.Documentation.ViewModels
{
	/// <summary>
	/// ViewModel for the API Documentation Browser
	/// </summary>
	public class ApiDocBrowserViewModel : INotifyPropertyChanged
	{
		private readonly ApiDocumentationService _docService;
		private string _searchText;
		private ApiClassInfo _selectedClass;
		private object _selectedMember;
		private bool _isDarkMode = true;
		private bool _includeClasses = true;
		private bool _includeMethods = true;
		private bool _includeProperties = true;
		private bool _isSettingsOpen;
		private ApiViewerSizeOption _selectedSizeOption;
		private ApiViewerSizeOption _customSizeOption;

		public ApiDocBrowserViewModel(ApiDocumentationSettings settings = null)
		{
			settings ??= new ApiDocumentationSettings();
			_docService = new ApiDocumentationService();
			_isDarkMode = settings.IsDarkMode;
			_customSizeOption = ApiViewerSizeOption.Custom(settings.Width, settings.Height);

			SearchCommand = new RelayCommand(ExecuteSearch);
			ClearSearchCommand = new RelayCommand(ExecuteClearSearch);
			CopySignatureCommand = new RelayCommand<object>(ExecuteCopyExample);

			SizeOptions.Add(new ApiViewerSizeOption("Compact", 980, 640));
			SizeOptions.Add(new ApiViewerSizeOption("Default", 1180, 780));
			SizeOptions.Add(new ApiViewerSizeOption("Wide", 1400, 850));
			SizeOptions.Add(new ApiViewerSizeOption("Large", 1600, 950));
			SizeOptions.Add(_customSizeOption);
			_selectedSizeOption = SizeOptions.FirstOrDefault(o => o.Matches(settings.Width, settings.Height)) ?? _customSizeOption;

			LoadApiClasses();
		}

		#region Properties

		/// <summary>
		/// All API classes
		/// </summary>
		public ObservableCollection<ApiClassInfo> AllClasses { get; } = new();

		/// <summary>
		/// Filtered/displayed API classes
		/// </summary>
		public ObservableCollection<ApiClassInfo> DisplayedClasses { get; } = new();

		public ObservableCollection<ApiMethodInfo> DisplayedMethods { get; } = new();

		public ObservableCollection<ApiPropertyInfo> DisplayedProperties { get; } = new();

		public ObservableCollection<ApiViewerSizeOption> SizeOptions { get; } = new();

		/// <summary>
		/// Whether the browser is in dark mode (true) or light mode (false).
		/// </summary>
		public bool IsDarkMode
		{
			get => _isDarkMode;
			set
			{
				if (_isDarkMode == value) return;
				_isDarkMode = value;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Search text
		/// </summary>
		public string SearchText
		{
			get => _searchText;
			set
			{
				if (_searchText != value)
				{
					_searchText = value;
					OnPropertyChanged();
					ExecuteSearch();
				}
			}
		}

		public bool IncludeClasses
		{
			get => _includeClasses;
			set
			{
				if (_includeClasses == value) return;
				_includeClasses = value;
				OnPropertyChanged();
				ExecuteSearch();
			}
		}

		public bool IncludeMethods
		{
			get => _includeMethods;
			set
			{
				if (_includeMethods == value) return;
				_includeMethods = value;
				OnPropertyChanged();
				ExecuteSearch();
			}
		}

		public bool IncludeProperties
		{
			get => _includeProperties;
			set
			{
				if (_includeProperties == value) return;
				_includeProperties = value;
				OnPropertyChanged();
				ExecuteSearch();
			}
		}

		public bool IsSettingsOpen
		{
			get => _isSettingsOpen;
			set
			{
				if (_isSettingsOpen == value) return;
				_isSettingsOpen = value;
				OnPropertyChanged();
			}
		}

		public ApiViewerSizeOption SelectedSizeOption
		{
			get => _selectedSizeOption;
			set
			{
				if (_selectedSizeOption == value || value == null) return;
				_selectedSizeOption = value;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Currently selected class
		/// </summary>
		public ApiClassInfo SelectedClass
		{
			get => _selectedClass;
			set
			{
				if (_selectedClass != value)
				{
					_selectedClass = value;
					OnPropertyChanged();
					RefreshDisplayedMembers();
				}
			}
		}

		/// <summary>
		/// Currently selected member (method or property)
		/// </summary>
		public object SelectedMember
		{
			get => _selectedMember;
			set
			{
				if (_selectedMember != value)
				{
					_selectedMember = value;
					OnPropertyChanged();
					OnPropertyChanged(nameof(SelectedMethod));
					OnPropertyChanged(nameof(SelectedProperty));
				}
			}
		}

		/// <summary>
		/// Selected method (if member is a method)
		/// </summary>
		public ApiMethodInfo SelectedMethod => SelectedMember as ApiMethodInfo;

		/// <summary>
		/// Selected property (if member is a property)
		/// </summary>
		public ApiPropertyInfo SelectedProperty => SelectedMember as ApiPropertyInfo;

		public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

		public bool HasResults => DisplayedClasses.Count > 0;

		public bool NoResults => HasSearchText && !HasResults;

		public int DisplayedMemberCount => DisplayedMethods.Count + DisplayedProperties.Count;

		public int ResultMemberCount => DisplayedClasses.Sum(CountMatchingMembers);

		public string SelectedClassFilterSummary
		{
			get
			{
				if (!HasSearchText || SelectedClass == null)
				{
					return null;
				}

				var memberLabel = DisplayedMemberCount == 1 ? "member" : "members";
				return $"Search filter active: showing {DisplayedMemberCount} matching {memberLabel} in {SelectedClass.Name}.";
			}
		}

		public string ResultsSummary
		{
			get
			{
				if (NoResults)
				{
					return $"No results for \"{SearchText}\"";
				}

				var classLabel = DisplayedClasses.Count == 1 ? "class" : "classes";
				var memberLabel = ResultMemberCount == 1 ? "member" : "members";
				return HasSearchText
					? $"{DisplayedClasses.Count} {classLabel}, {ResultMemberCount} matching {memberLabel}"
					: $"{DisplayedClasses.Count} {classLabel} available";
			}
		}

		public void UpdateCurrentSize(double width, double height)
		{
			var matchingPreset = SizeOptions.FirstOrDefault(o => !o.IsCustom && o.Matches(width, height));
			_customSizeOption.Update(width, height);
			OnPropertyChanged(nameof(SizeOptions));

			var nextSelection = matchingPreset ?? _customSizeOption;
			if (!ReferenceEquals(_selectedSizeOption, nextSelection))
			{
				_selectedSizeOption = nextSelection;
				OnPropertyChanged(nameof(SelectedSizeOption));
			}
		}

		#endregion

		#region Commands

		public ICommand SearchCommand { get; }
		public ICommand ClearSearchCommand { get; }
		public ICommand CopySignatureCommand { get; }

		#endregion

		#region Methods

		/// <summary>
		/// Load all API classes from reflection
		/// </summary>
		private void LoadApiClasses()
		{
			try
			{
				var classes = _docService.GetAllApiClasses();

				AllClasses.Clear();
				DisplayedClasses.Clear();

				foreach (var apiClass in classes)
				{
					AllClasses.Add(apiClass);
					DisplayedClasses.Add(apiClass);
				}

				SelectedClass = DisplayedClasses.FirstOrDefault();
				RefreshResultState();

				Console.WriteLine($"[ApiDocs] Loaded {AllClasses.Count} API classes");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ApiDocs] Error loading API classes: {ex.Message}");
			}
		}

		/// <summary>
		/// Execute search/filter
		/// </summary>
		private void ExecuteSearch()
		{
			DisplayedClasses.Clear();
			var search = SearchText?.Trim();

			if (string.IsNullOrWhiteSpace(search))
			{
				foreach (var apiClass in AllClasses)
				{
					DisplayedClasses.Add(apiClass);
				}
			}
			else
			{
				foreach (var apiClass in AllClasses)
				{
					if (ClassMatches(apiClass, search) || CountMatchingMembers(apiClass, search) > 0)
					{
						DisplayedClasses.Add(apiClass);
					}
				}
			}

			if (SelectedClass == null || !DisplayedClasses.Contains(SelectedClass))
			{
				SelectedClass = DisplayedClasses.FirstOrDefault();
			}
			else
			{
				RefreshDisplayedMembers();
			}

			RefreshResultState();
		}

		public void SelectFirstResult()
		{
			if (DisplayedClasses.Count == 0)
			{
				return;
			}

			SelectedClass = DisplayedClasses[0];
		}

		/// <summary>
		/// Clear search
		/// </summary>
		private void ExecuteClearSearch()
		{
			SearchText = string.Empty;
		}

		private void RefreshDisplayedMembers()
		{
			DisplayedMethods.Clear();
			DisplayedProperties.Clear();

			if (SelectedClass == null)
			{
				RefreshResultState();
				return;
			}

			var search = SearchText?.Trim();
			var hasSearch = !string.IsNullOrWhiteSpace(search);

			if (IncludeMethods)
			{
				foreach (var method in SelectedClass.Methods.Where(m => !hasSearch || MethodMatches(m, search)))
				{
					DisplayedMethods.Add(method);
				}
			}

			if (IncludeProperties)
			{
				foreach (var property in SelectedClass.Properties.Where(p => !hasSearch || PropertyMatches(p, search)))
				{
					DisplayedProperties.Add(property);
				}
			}

			RefreshResultState();
		}

		private bool ClassMatches(ApiClassInfo apiClass, string search)
		{
			return IncludeClasses &&
			       (Contains(apiClass.Name, search) ||
			        Contains(apiClass.FullName, search) ||
			        Contains(apiClass.Summary, search));
		}

		private int CountMatchingMembers(ApiClassInfo apiClass)
		{
			var search = SearchText?.Trim();
			return string.IsNullOrWhiteSpace(search)
				? (IncludeMethods ? apiClass.Methods.Count : 0) + (IncludeProperties ? apiClass.Properties.Count : 0)
				: CountMatchingMembers(apiClass, search);
		}

		private int CountMatchingMembers(ApiClassInfo apiClass, string search)
		{
			var methodCount = IncludeMethods ? apiClass.Methods.Count(m => MethodMatches(m, search)) : 0;
			var propertyCount = IncludeProperties ? apiClass.Properties.Count(p => PropertyMatches(p, search)) : 0;
			return methodCount + propertyCount;
		}

		private bool MethodMatches(ApiMethodInfo method, string search)
		{
			return Contains(method.Name, search);
		}

		private bool PropertyMatches(ApiPropertyInfo property, string search)
		{
			return Contains(property.Name, search);
		}

		private static bool Contains(string value, string search)
		{
			return !string.IsNullOrWhiteSpace(value) &&
			       value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private void RefreshResultState()
		{
			OnPropertyChanged(nameof(HasSearchText));
			OnPropertyChanged(nameof(HasResults));
			OnPropertyChanged(nameof(NoResults));
			OnPropertyChanged(nameof(DisplayedMemberCount));
			OnPropertyChanged(nameof(ResultMemberCount));
			OnPropertyChanged(nameof(ResultsSummary));
			OnPropertyChanged(nameof(SelectedClassFilterSummary));
		}

		/// <summary>
		/// Copy example usage to clipboard
		/// </summary>
		private void ExecuteCopyExample(object member)
		{
			try
			{
				string example = null;

				if (member is ApiMethodInfo method)
				{
					example = method.Example;
				}
				else if (member is ApiPropertyInfo property)
				{
					example = property.Example;
				}

				if (!string.IsNullOrEmpty(example))
				{
					System.Windows.Clipboard.SetText(example);
					Console.WriteLine($"[ApiDocs] Copied example usage to clipboard");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ApiDocs] Error copying example usage: {ex.Message}");
			}
		}

		#endregion

		#region INotifyPropertyChanged

		public event PropertyChangedEventHandler PropertyChanged;

		protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		#endregion
	}

	/// <summary>
	/// Simple relay command implementation
	/// </summary>
	public class RelayCommand : ICommand
	{
		private readonly Action _execute;
		private readonly Func<bool> _canExecute;

		public RelayCommand(Action execute, Func<bool> canExecute = null)
		{
			_execute = execute ?? throw new ArgumentNullException(nameof(execute));
			_canExecute = canExecute;
		}

		public event EventHandler CanExecuteChanged
		{
			add => CommandManager.RequerySuggested += value;
			remove => CommandManager.RequerySuggested -= value;
		}

		public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

		public void Execute(object parameter) => _execute();
	}

	/// <summary>
	/// Relay command with parameter
	/// </summary>
	public class RelayCommand<T> : ICommand
	{
		private readonly Action<T> _execute;
		private readonly Func<T, bool> _canExecute;

		public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
		{
			_execute = execute ?? throw new ArgumentNullException(nameof(execute));
			_canExecute = canExecute;
		}

		public event EventHandler CanExecuteChanged
		{
			add => CommandManager.RequerySuggested += value;
			remove => CommandManager.RequerySuggested -= value;
		}

		public bool CanExecute(object parameter) => _canExecute?.Invoke((T)parameter) ?? true;

		public void Execute(object parameter) => _execute((T)parameter);
	}

	public sealed class ApiViewerSizeOption : INotifyPropertyChanged
	{
		public ApiViewerSizeOption(string name, double width, double height, bool isCustom = false)
		{
			Name = name;
			Width = width;
			Height = height;
			IsCustom = isCustom;
		}

		public string Name { get; }
		public double Width { get; private set; }
		public double Height { get; private set; }
		public bool IsCustom { get; }
		public string DisplayName => IsCustom ? $"Custom ({Width:0} x {Height:0})" : $"{Name} ({Width:0} x {Height:0})";

		public static ApiViewerSizeOption Custom(double width, double height) => new("Custom", width, height, isCustom: true);

		public bool Matches(double width, double height)
		{
			return Math.Abs(Width - width) < 2 && Math.Abs(Height - height) < 2;
		}

		public void Update(double width, double height)
		{
			if (!IsCustom || Matches(width, height))
			{
				return;
			}

			Width = width;
			Height = height;
			OnPropertyChanged(nameof(Width));
			OnPropertyChanged(nameof(Height));
			OnPropertyChanged(nameof(DisplayName));
		}

		public override string ToString() => DisplayName;

		public event PropertyChangedEventHandler PropertyChanged;

		private void OnPropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
