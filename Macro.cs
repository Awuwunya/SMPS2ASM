using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace smps2asm {
	class Macro : Dic {
		public string[] arguments { get; }
		public byte[] flags { get; }
		private string macro;

		public Macro(byte[] flag, string name, string[] args, object d) : base(d) {
			flags = flag;
			macro = name;
			arguments = args;
		}

		public int requiredBytes() {
			return flags.Length;
		}

		public bool isThisFlag(byte[] dat) {
			if(dat.Length != flags.Length) {
				throw new ArgumentException("Incorrect argument number!");
			}

			if (dat.Length == 0) return true;

			for(int i = 0;i < dat.Length;i++) {
				if(dat[i] != flags[i]) {
					return false;
				}
			}

			return true;
		}

		public string getArgument(int i) {
			return arguments[i];
		}

		public string getName() {
			return macro;
		}
	}
}
