using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;

namespace Keg2Smug
{
    class KodakAlbum : IEquatable<KodakAlbum>
    {
        public string albumId;
        public string albumName;
        public string albumDescription;
        public List<KodakPhoto> photos;

        public KodakAlbum(string albumId, string albumName)
        {
            this.albumId = albumId;
            this.albumName = albumName;
            this.albumDescription = null;
            photos = null;
        }

        /// <summary>
        /// Write this album to disk
        /// </summary>
        public void WriteToDisk(string temporaryLocation)
        {
            if (!Directory.Exists(string.Format("{0}\\{1}", temporaryLocation, albumId)))
            {
                Directory.CreateDirectory(string.Format("{0}\\{1}", temporaryLocation, albumId));
            }

            string dataFile = string.Format("{0}\\{1}\\keg2smug.{2}.data", temporaryLocation, albumId, albumId);

            if (File.Exists(dataFile))
            {
                Console.WriteLine("File {0} exists, overwriting...", dataFile);
            }

            using (FileStream file = File.Create(dataFile))
            {
                using (StreamWriter writer = new StreamWriter(file))
                {
                    // Line 1: AlbumId
                    // Line 2: AlbumName
                    // Line 3: AlbumDescription
                    // Line 4 onwards: Photo data in PhotoId|Description format
                    writer.WriteLine(albumId);
                    writer.WriteLine(albumName);
                    writer.WriteLine(albumDescription);
                    foreach (KodakPhoto photo in photos)
                    {
                        photo.WriteToDisk(writer);
                    }
                }
            }
        }


        public bool ReadFromDisk(string temporaryLocation)
        {
            string dataFile = string.Format("{0}\\{1}\\keg2smug.{2}.data", temporaryLocation, albumId, albumId);

            if (File.Exists(dataFile))
            {
                using (FileStream file = File.OpenRead(dataFile))
                {
                    using (StreamReader reader = new StreamReader(file))
                    {
                        // AlbumId
                        string albumIdTemp = reader.ReadLine();
                        Debug.Assert(string.Compare(albumId, albumIdTemp, true) == 0);

                        albumName = reader.ReadLine();
                        albumDescription = reader.ReadLine();

                        // Read the photos
                        photos = new List<KodakPhoto>();
                        KodakPhoto photo = null;
                        do
                        {
                            photo = KodakPhoto.ReadFromDisk(reader);
                            if (photo != null)
                            {
                                photos.Add(photo);
                            }
                        } while (photo != null);
                    }
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        public void CreateAlbumFolder(string temporaryLocation)
        {
            if (!Directory.Exists(string.Format("{0}\\{1}", temporaryLocation, albumId)))
            {
                Directory.CreateDirectory(string.Format("{0}\\{1}", temporaryLocation, albumId));
            }
        }


        #region IEquatable<KodakAlbum> Members

        public bool Equals(KodakAlbum other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Compare(albumId, other.albumId, true) == 0;
        }

        #endregion
    }
}
