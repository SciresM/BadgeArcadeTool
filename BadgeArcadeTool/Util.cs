using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace BadgeArcadeTool
{
    public class Util
    {
        private static StreamWriter log;
        private static string logFile;

        public static void NewLogFile(string programname)
        {
            var now = DateTime.Now;
            logFile = $"logs/{now.ToString("MMMM dd, yyyy - HH-mm-ss")}.log";
            log = new StreamWriter(logFile, false, Encoding.Unicode);
            Log(programname);
            Log($"{now.ToString("MMMM dd, yyyy - HH-mm-ss")}");
        }

        public static void CloseLogFile(bool keeplog)
        {
            log.Close();
            if (!keeplog)
                File.Delete(logFile);
        }

        public static void Log(string msg, bool newline = true)
        {
            if (newline)
            {
                Console.WriteLine(msg);
                log.WriteLine(msg);
            }
            else
            {
                Console.Write(msg);
                log.Write(msg);
            }
        }

        public static string Serialize<T>(T value, string filename = null)
        {

            if (value == null)
            {
                return null;
            }

            var serializer = new XmlSerializer(typeof(T));

            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true,
                IndentChars = "   ",
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
            };

            using (var textWriter = new StringWriter())
            {
                using (var xmlWriter = XmlWriter.Create(textWriter, settings))
                    serializer.Serialize(xmlWriter, value);
                var xml = textWriter.ToString();
                if (String.IsNullOrEmpty(filename))
                    return xml;

                using (var fileWriter = new FileStream(filename, FileMode.Create))
                using (var streamWriter = new StreamWriter(fileWriter))
                    streamWriter.Write(xml);

                return xml;
            }
        }

        public static T DeserializeFile<T>(string filename)
        {
            if (String.IsNullOrEmpty(filename) || !File.Exists(filename))
                return default(T);

            using (var fileReader = new FileStream(filename, FileMode.Open))
            using (var streamReader = new StreamReader(fileReader))
                return Deserialize<T>(streamReader.ReadToEnd());
        }

        public static T Deserialize<T>(string xml)
        {

            if (String.IsNullOrEmpty(xml))
                return default(T);

            var serializer = new XmlSerializer(typeof(T));

            var settings = new XmlReaderSettings();

            using (var textReader = new StringReader(xml))
            using (var xmlReader = XmlReader.Create(textReader, settings))
                return (T)serializer.Deserialize(xmlReader);
        }
    }
}