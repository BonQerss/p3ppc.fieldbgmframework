using System.ComponentModel;
using p3ppc.fieldbgmframework.Template.Configuration;

namespace p3ppc.fieldbgmframework.Configuration
{
    public class Config : Configurable<Config>
    {
        [DisplayName("Debug Mode")]
        [Description("Logs the loaded BGM entries and which one plays.")]
        [DefaultValue(false)]
        public bool DebugEnabled { get; set; } = false;
    }

    public class ConfiguratorMixin : ConfiguratorMixinBase
    {
    }
}
