using MS3DPRINT.Manager.Core.Clients;
using MS3DPRINT.Manager.Core.Naming;

namespace MS3DPRINT.Manager.App.ViewModels;

public enum ProjectCreationStep
{
    Client,
    Project,
    Confirmation
}

public sealed class ProjectCreationFlow : ObservableObject
{
    private ProjectCreationStep _step = ProjectCreationStep.Client;
    private ClientSummary? _selectedClient;
    private int _year;
    private string _projectName = string.Empty;
    private DateOnly? _dueDate;
    private string _description = string.Empty;

    public ProjectCreationFlow(int year) => Year = year;

    public ProjectCreationStep Step
    {
        get => _step;
        private set
        {
            if (!SetProperty(ref _step, value)) return;
            OnPropertyChanged(nameof(CanContinue));
        }
    }

    public ClientSummary? SelectedClient
    {
        get => _selectedClient;
        private set
        {
            if (!SetProperty(ref _selectedClient, value)) return;
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    public int Year
    {
        get => _year;
        set
        {
            if (!SetProperty(ref _year, value)) return;
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (!SetProperty(ref _projectName, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    public DateOnly? DueDate { get => _dueDate; set => SetProperty(ref _dueDate, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value ?? string.Empty); }

    public bool CanContinue => Step switch
    {
        ProjectCreationStep.Client => HasCompleteClient,
        ProjectCreationStep.Project => HasValidProject,
        _ => false
    };

    public bool CanCreate => HasCompleteClient && HasValidProject;

    public void SelectClient(ClientSummary? client) => SelectedClient = client;

    public void MoveToClient() => Step = ProjectCreationStep.Client;

    public void MoveToProject()
    {
        if (!HasCompleteClient) throw new InvalidOperationException("Sélectionnez un client avec une fiche complète.");
        Step = ProjectCreationStep.Project;
    }

    public void MoveToConfirmation()
    {
        if (!HasCompleteClient) throw new InvalidOperationException("Sélectionnez un client avec une fiche complète.");
        if (!HasValidProject) throw new InvalidOperationException("Renseignez une année et un nom de projet valides.");
        Step = ProjectCreationStep.Confirmation;
    }

    private bool HasCompleteClient => SelectedClient?.Profile is not null;
    private bool HasValidProject => Year is >= 2000 and <= 9999 && NameNormalizer.Normalize(ProjectName).Length > 0;
}
