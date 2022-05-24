﻿
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;

namespace MidiBard.HSC
{
    public class FileHelpers
    {

        public static void WriteText(string text, string fileName)
        {
            File.AppendAllText(fileName, text);
        }

        public static void Save(object obj, string fileName)
        {
            var dirName = Path.GetDirectoryName(fileName);

            if (!Directory.Exists(dirName))
                Directory.CreateDirectory(dirName);

            var json = JsonConvert.SerializeObject(obj);
            File.WriteAllText(fileName, json);
        }

        public static T Load<T>(string filePath)
        {
            if (!File.Exists(filePath))
                return default(T);

            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<T>(json);

        }

        public static bool IsDirectory(string path)
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.Directory) == FileAttributes.Directory;
        }

    }
}