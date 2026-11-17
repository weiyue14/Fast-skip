using System;
using System.IO;
using System.Xml.Serialization;
using System.Windows.Forms;

namespace FastSkip
{
    public sealed class Settings
    {
        public uint Modifiers = 1;
        public uint Key = (uint)Keys.X;
        public static string DefaultPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FastSkip.settings.xml"); } }
        public static Settings Load(string path)
        {
            try
            {
                using (var file = File.OpenRead(path))
                {
                    var value = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(file);
                    if (value.Modifiers == 0 || (value.Modifiers & ~7u) != 0 || !AllowedKey(value.Key)) return new Settings();
                    return value;
                }
            }
            catch { return new Settings(); }
        }
        public static bool AllowedKey(uint key)
        {
            return (key >= 0x41 && key <= 0x5A) || (key >= 0x30 && key <= 0x39) ||
                   (key >= 0x70 && key <= 0x7A) || key == 0x20;
        }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            try
            {
                using (var file = File.Create(temp)) new XmlSerializer(typeof(Settings)).Serialize(file, this);
                if (File.Exists(path))
                {
                    try { File.Replace(temp, path, null); }
                    catch (UnauthorizedAccessException) { File.Copy(temp, path, true); }
                }
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public string Display()
        {
            return ((Modifiers & 2) != 0 ? "Ctrl + " : "") + ((Modifiers & 1) != 0 ? "Alt + " : "") +
                   ((Modifiers & 4) != 0 ? "Shift + " : "") + (Key == 32 ? "空格" : ((Keys)Key).ToString());
        }
    }
}
