using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;  

namespace BadgeArcadeTool {
    class Options {
        [Option('i', "ip", DefaultValue = "192.168.1.137", HelpText = "The IP address of the 3DS running the crypto server.", Required = false)]
        public string InputIP { get; set; }

        [HelpOption]
        public string GetUsage() {
            var usage = new StringBuilder();
            usage.AppendLine("Quickstart Application 1.0");
            usage.AppendLine("Read user manual for usage instructions...");
            return usage.ToString();
        }
    }
}
