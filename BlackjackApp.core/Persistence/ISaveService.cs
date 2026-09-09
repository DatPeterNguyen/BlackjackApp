namespace BlackjackApp.core.Persistence;

public interface ISaveService
{
    SaveData Load();

    void Save(SaveData data);
}
