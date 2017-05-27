using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using CommandLine;
using CommandLine.Text;
using static System.AppDomain;

namespace BadgeArcadeTool {

    [XmlRoot("ProgramOptions")]
    [Serializable]
    public class Options {

        [XmlAttribute("BootRom")]
        [Option('b', "boot9", HelpText = "Path and file name of the boot rom", Required = false)]
        public string Boot9 { get; set; }

        [XmlIgnore]
        [Option('r',"reset",HelpText = "Reset all settings to Default")]
        public bool Reset { get; set; }

        [XmlIgnore]
        [Option]
        public bool help { get; set; }

        [HelpOption]
        public string GetUsage() {
            var helptext = new HelpText
            {
                AdditionalNewLineAfterOption = true,
                AddDashesToOption = true,
                MaximumDisplayWidth = Console.WindowWidth
            };
            helptext.AddPreOptionsLine("");
            helptext.AddPreOptionsLine($"Usage: {CurrentDomain.FriendlyName} options [--help for more information]");

            if (help)
                helptext.AddOptions(this);
            return helptext;
        }
    }
}
