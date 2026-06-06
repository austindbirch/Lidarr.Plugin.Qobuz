using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace NzbDrone.Core.Download.Clients.Qobuz
{
    public class QobuzSettingsValidator : AbstractValidator<QobuzSettings>
    {
        public QobuzSettingsValidator()
        {
            RuleFor(x => x.DownloadPath).IsValidPath();
            RuleFor(x => x.CompletedDownloadDirectory).IsValidPath().When(x => !string.IsNullOrWhiteSpace(x.CompletedDownloadDirectory));
        }
    }

    public class QobuzSettings : IProviderConfig
    {
        private static readonly QobuzSettingsValidator Validator = new QobuzSettingsValidator();

        [FieldDefinition(0, Label = "Download Path", Type = FieldType.Textbox)]
        public string DownloadPath { get; set; } = "";

        [FieldDefinition(3, Label = "Save Synced Lyrics", HelpText = "Saves synced lyrics to a separate .lrc file if available. Requires .lrc to be allowed under Import Extra Files.", Type = FieldType.Checkbox)]
        public bool SaveSyncedLyrics { get; set; } = false;

        [FieldDefinition(4, Label = "Use LRCLIB as Lyric Provider", HelpText = "Qobuz does not supply lyrics, this setting will enable grabbing lyric data from LRCLIB.", Type = FieldType.Checkbox)]
        public bool UseLRCLIB { get; set; } = false;

        [FieldDefinition(5, Label = "Require Complete Album", Type = FieldType.Checkbox, HelpText = "If any track cannot be downloaded, mark the entire album as failed rather than completing with missing tracks. Recommended; lets Lidarr retry or try another release instead of importing a partial album.")]
        public bool RequireCompleteAlbum { get; set; } = true;

        [FieldDefinition(6, Label = "Completed Download Directory", Type = FieldType.Textbox, HelpText = "When set, a finished album is moved to <this folder>/Artist/Album once all files are closed. Leave blank to disable.")]
        public string CompletedDownloadDirectory { get; set; } = "";

        [FieldDefinition(7, Label = "Post-Download Script", Type = FieldType.Textbox, HelpText = "Optional executable run after a successful move. Receives the final album directory as the first argument and the Qobuz album ID as the second. Leave blank to disable.")]
        public string PostDownloadScript { get; set; } = "";

        [FieldDefinition(8, Label = "Post-Download Script Timeout", Type = FieldType.Number, Advanced = true, HelpText = "Seconds to wait for the post-download script before killing it. 0 disables the timeout.")]
        public int PostDownloadScriptTimeout { get; set; } = 300;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
