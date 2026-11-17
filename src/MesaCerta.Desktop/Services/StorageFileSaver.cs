using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace MesaCerta.Desktop.Services;

public sealed class StorageFileSaver(TopLevel owner) : IFileSaver
{
    public async Task<bool> SaveTextAsync(string title, string suggestedName, string content)
    {
        var downloads = await owner.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = downloads,
            DefaultExtension = "xml",
            FileTypeChoices = [new FilePickerFileType("XML NFC-e") { Patterns = ["*.xml"] }],
        });
        if (file is null) return false;
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await stream.WriteAsync(new UTF8Encoding(false).GetBytes(content));
        return true;
    }
}
