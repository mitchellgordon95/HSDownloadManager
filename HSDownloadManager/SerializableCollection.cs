using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;

namespace HSDownloadManager
{
    [Serializable]
    public class SerializableCollection<T> : ObservableCollection<T>
    {
        public void LoadFromFile(string fileName)
        {
            if (!File.Exists(fileName))
                throw new FileNotFoundException("File not found", fileName);

            Clear();

            try
            {
                IFormatter formatter = new BinaryFormatter();
                Stream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                ObservableCollection<T> list = (ObservableCollection<T>)formatter.Deserialize(stream);
                foreach (T o in list)
                    Add(o);
                stream.Close();
            }
            catch { }
        }

        public void SaveToFile(string fileName)
        {
            if (File.Exists(fileName))
                File.Delete(fileName);

            IFormatter formatter = new BinaryFormatter();
            Stream stream = new FileStream(fileName, FileMode.CreateNew);
            formatter.Serialize(stream, this);
            stream.Close();
        }
    }
}
