using System;
using System.Windows.Input;

namespace MESharp.Commands
{
	/// <summary>
	/// A simple ICommand implementation for wiring up ViewModel commands.
	/// </summary>
	public class RelayCommand : ICommand
	{
		private readonly Action<object> _execute;
		private readonly Func<object, bool> _canExecute;

		/// <summary>
		/// Raised whenever CanExecute should be re-evaluated.
		/// </summary>
		public event EventHandler CanExecuteChanged
		{
			add => CommandManager.RequerySuggested += value;
			remove => CommandManager.RequerySuggested -= value;
		}

		///// <summary>
		///// Creates a new RelayCommand which is always enabled.
		///// </summary>
		//public RelayCommand(Action<object> execute, Func<object, bool> value)
		//	: this(execute, _ => true)
		//{
		//}

		/// <summary>
		/// Creates a new RelayCommand.
		/// </summary>
		/// <param name="execute">Action to run when Execute is called.</param>
		/// <param name="canExecute">Predicate to determine if the command is allowed.</param>
		public RelayCommand(Action<object> execute, Func<object, bool> canExecute)
		{
			_execute    = execute  ?? throw new ArgumentNullException(nameof(execute));
			_canExecute = canExecute ?? throw new ArgumentNullException(nameof(canExecute));
		}

		/// <summary>
		/// Creates a new RelayCommand that is always executable.
		/// </summary>
		public RelayCommand(Action<object> execute)
			: this(execute, _ => true)
		{
		}

		/// <summary>
		/// ICommand.CanExecute
		/// </summary>
		public bool CanExecute(object parameter)
			=> _canExecute(parameter);

		/// <summary>
		/// ICommand.Execute
		/// </summary>
		public void Execute(object parameter)
			=> _execute(parameter);
	}
}
