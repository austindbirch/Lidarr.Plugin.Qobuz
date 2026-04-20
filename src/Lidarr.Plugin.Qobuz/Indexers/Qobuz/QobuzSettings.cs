using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Indexers.Qobuz
{
    public class QobuzIndexerSettingsValidator : AbstractValidator<QobuzIndexerSettings>
    {
        public QobuzIndexerSettingsValidator()
        {
        }
    }

    public class QobuzIndexerSettings : IIndexerSettings
    {
        private static readonly QobuzIndexerSettingsValidator Validator = new QobuzIndexerSettingsValidator();

        [FieldDefinition(0, Label = "Qobuz Email", Type = FieldType.Textbox, HelpTextWarning = "Email/password login only supports search. For downloads, use User ID + Auth Token instead.")]
        public string Email { get; set; } = "";

        [FieldDefinition(1, Label = "Qobuz Password (MD5)", Type = FieldType.Textbox, HelpText = "Your Qobuz password hashed with MD5. Email/password mode does not support downloading — use User ID + Auth Token for full functionality.")]
        public string MD5Password { get; set; } = "";

        [FieldDefinition(2, Label = "User ID", Type = FieldType.Textbox, HelpText = "Your Qobuz numeric user ID. Find it in browser DevTools at play.qobuz.com: open Network tab, look for any API response containing 'user' with an 'id' field.")]
        public string UserID { get; set; }

        [FieldDefinition(3, Label = "User Auth Token", Type = FieldType.Textbox, HelpText = "Your Qobuz auth token. Get it from play.qobuz.com: open DevTools → Network tab → look for requests with an 'X-User-Auth-Token' header, or check Local Storage for 'user_auth_token'. This token enables both search and downloads.")]
        public string UserAuthToken { get; set; }

        [FieldDefinition(4, Label = "App ID", Type = FieldType.Textbox, Placeholder = "Optional")]
        public string AppID { get; set; }

        [FieldDefinition(5, Label = "App Secret", Type = FieldType.Textbox, Placeholder = "Optional")]
        public string AppSecret { get; set; }

        [FieldDefinition(6, Type = FieldType.Number, Label = "Early Download Limit", Unit = "days", HelpText = "Time before release date Lidarr will download from this indexer, empty is no limit", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        // this is hardcoded so this doesn't need to exist except that it's required by the interface
        public string BaseUrl { get; set; } = "";

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
