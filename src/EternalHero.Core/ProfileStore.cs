using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace EternalHero.Core
{
    public sealed class ProfileStore
    {
        public string FilePath { get; }
        public string BackupPath => FilePath + ".bak";

        public ProfileStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A save path is required", nameof(path));
            FilePath = Path.GetFullPath(path);
        }

        private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(
            typeof(HeroProfile), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public HeroProfile Load()
        {
            if (!File.Exists(FilePath)) return File.Exists(BackupPath) ? Read(BackupPath) : new HeroProfile();
            try { return Read(FilePath); }
            catch (Exception) when (File.Exists(BackupPath)) { return Read(BackupPath); }
        }

        private static HeroProfile Read(string path)
        {
            using (var file = File.OpenRead(path))
            {
                var profile = (HeroProfile)Serializer().ReadObject(file);
                if (profile == null || profile.SchemaVersion != 1 ||
                    profile.LifetimeExperience < 0 || profile.AvailableEssence < 0 || profile.EarnedEssence < 0)
                    throw new InvalidDataException("Unsupported or invalid profile");
                return profile;
            }
        }

        public void Save(HeroProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var temporary = FilePath + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    Serializer().WriteObject(file, profile);
                    file.Flush(true);
                }
                if (File.Exists(FilePath))
                {
                    try { File.Replace(temporary, FilePath, BackupPath); }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(FilePath, BackupPath, true);
                        File.Delete(FilePath);
                        File.Move(temporary, FilePath);
                    }
                }
                else File.Move(temporary, FilePath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
