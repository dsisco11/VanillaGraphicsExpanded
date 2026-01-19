namespace VanillaGraphicsExpanded.Rendering;

internal interface IUploadScheduler
{
    void Sort(UploadCommand[] commands, int count);
}
