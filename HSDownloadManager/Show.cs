using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSDownloadManager
{
    [Serializable]
	public class Show : INotifyPropertyChanged
	{

        [field:NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        private void SetMember<T>(ref T mem, T val, string propName)
        {
            // Do nothing if the value is already set
            if (EqualityComparer<T>.Default.Equals(mem, val)) return;

            mem = val;

            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
                    
        }

		public string Name {
            get { return _name; }
            set { SetMember(ref _name, value, "Name"); }
        }
        private string _name;


		public int NextEpisode {
            get { return _nextEpisode; }
            set { SetMember(ref _nextEpisode, value, "NextEpisode"); }
        }
        private int _nextEpisode;

		public DateTime AirsOn {
            get { return _airsOn; }
            set { SetMember(ref _airsOn, value, "AirDate"); }
        }
        private DateTime _airsOn;

		public string Status {
            get { return _status; }
            set { SetMember(ref _status, value, "Status"); }
        }
        private string _status;

    }
}
