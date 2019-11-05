using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Casparcg.Core.Device
{
	public enum MediaType
    {
        STILL,
        MOVIE,
        AUDIO,
        ALL
	}

	public class MediaInfo: INotifyPropertyChanged
	{
		internal MediaInfo(string folder, string name, MediaType type, long size, DateTime updated, string timecode, long frames, double fps)
		{
			Folder = folder;
			Name = name;
			Size = size;
			LastUpdated = updated;
			Type = type;
            Timecode = timecode;
            Fps = fps;
            Frames = frames;
        }

        private string timecode_;
        public string Timecode
        {
            get { return timecode_; }
            internal set { timecode_ = value; }
        }

        private long frames_;
        public long Frames
        {
            get { return frames_; }
            internal set { frames_ = value; }
        }

        private double fps_;
        public double Fps
        {
            get { return fps_; }
            internal set { fps_ = value; }
        }

        private string folder_;
		public string Folder
		{
			get { return folder_; }
			internal set { folder_ = value; }
		}
		private string name_;
		public string Name
		{
			get { return name_; }
			internal set { name_ = value; }
		}
		public string FullName
		{
			get
			{
				if (!String.IsNullOrEmpty(Folder))
					return Folder + '\\' + Name;
				else
					return Name;
			}
		}
		private MediaType type_;
        public MediaType Type
		{
			get { return type_; }
			set { type_ = value; }
		}

		private long size_;
		public long Size
		{
			get { return size_; }
			internal set { size_ = value; }
        }

        private DateTime updated_;
        public DateTime LastUpdated
        {
            get { return updated_; }
            internal set { updated_ = value; }
        }

        private string thumbnail_ = null;
        public string Thumbnail
        {
            get { return thumbnail_; }
            set
            {
                thumbnail_ = value;
                OnPropertyChanged("Thumbnail");
            }
        }

        public string ElementType { get; } = "Media";

        public override string ToString()
		{
			return FullName;
		}

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
	}
}
