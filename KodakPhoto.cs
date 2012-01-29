using System.IO;
using System.Diagnostics;
using System;
namespace Keg2Smug
{
    class KodakPhoto
    {
        public string photoId;
        public string photoCaption = "";
        public string fileName = null;

        public KodakPhoto()
        {
            this.photoId = null;
        }

        public KodakPhoto(string photoId)
        {
            this.photoId = photoId;
        }

        public void WriteToDisk(StreamWriter writer)
        {
            writer.WriteLine("{0}|##|{1}|##|{2}", photoId, photoCaption, fileName);
        }

        public static KodakPhoto ReadFromDisk(StreamReader reader)
        {
            string data = reader.ReadLine();
            if (data == null)
            {
                return null;
            }

            string[] splitData = data.Split(new string[] { "|##|" }, StringSplitOptions.None);
            Debug.Assert(splitData.Length == 3);
            KodakPhoto photo = new KodakPhoto(splitData[0]);
            photo.photoCaption = splitData[1];
            photo.fileName = splitData[2];
            return photo;
        }

    }
}
