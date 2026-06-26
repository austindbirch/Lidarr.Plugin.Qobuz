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

        [FieldDefinition(9, Label = "Artwork Size", Type = FieldType.Select, SelectOptions = typeof(QobuzArtworkSize), HelpText = "Cover-art resolution embedded/saved with downloads. 'Custom' downscales Qobuz's original to the resolution below.")]
        public int ArtworkSize { get; set; } = (int)QobuzArtworkSize.Large;

        [FieldDefinition(10, Label = "Custom Artwork Resolution", Type = FieldType.Number, Advanced = true, Unit = "px", HelpText = "Used only when Artwork Size is Custom: the original cover is downscaled to fit within this many pixels.")]
        public int CustomArtworkResolution { get; set; } = 1000;

        [FieldDefinition(11, Label = "Artwork Placement", Type = FieldType.Select, SelectOptions = typeof(QobuzArtworkPlacement), HelpText = "Embed the cover in each track, write a cover.jpg sidecar in the album folder, or both.")]
        public int ArtworkPlacement { get; set; } = (int)QobuzArtworkPlacement.Embed;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }

    public enum QobuzArtworkSize
    {
        [FieldOption(label: "Small (230px)")]
        Small = 0,

        [FieldOption(label: "Large (600px)")]
        Large = 1,

        [FieldOption(label: "Original (max)")]
        Original = 2,

        [FieldOption(label: "Custom (downscale)")]
        Custom = 3
    }

    public enum QobuzArtworkPlacement
    {
        [FieldOption(label: "Embed in tracks")]
        Embed = 0,

        [FieldOption(label: "Sidecar (cover.jpg)")]
        Sidecar = 1,

        [FieldOption(label: "Both")]
        Both = 2
    }
}
