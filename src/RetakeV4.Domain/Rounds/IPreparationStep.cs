namespace RetakeV4.Domain.Rounds;

public interface IPreparationStep
{
    string Name { get; }

    int Order { get; }

    PreparationContext Execute(PreparationContext context);
}
