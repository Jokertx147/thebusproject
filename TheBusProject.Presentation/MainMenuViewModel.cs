using System;
using System.Windows.Input;

namespace TheBusProject.Presentation
{
    public class MainMenuViewModel : ObservableObject
    {
        public MainMenuViewModel(ShellViewModel shell, Action exit)
        {
            NewGameCommand = new RelayCommand(shell.ShowNewGame);
            LoadGameCommand = new RelayCommand(shell.ShowLoadGame);
            ScenariosCommand = new RelayCommand(shell.ShowScenarios);
            ExitCommand = new RelayCommand(exit);
        }

        public string Title => "The Carrier Project";

        public ICommand NewGameCommand { get; }

        public ICommand LoadGameCommand { get; }

        public ICommand ScenariosCommand { get; }

        public ICommand ExitCommand { get; }
    }
}
