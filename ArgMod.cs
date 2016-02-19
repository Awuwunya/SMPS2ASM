using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace smps2asm {
	class ArgMod : Dic {
		public int ID { get; }
		public ArgMod(object d, int id) : base(d) {
			ID = id;
		}

		public string GetLable(int value) {
			foreach (KeyValuePair<string, object> o in GetKeyset()) {
				if ((int) o.Value == value) {
					return o.Key as String;
				}
			}

			return null;
		}
	}
}
