using System.Collections.Generic;

namespace smps2asm {
	internal class Repeat : Dic {
		public string times { get; }

		public Repeat(string t, Dictionary<string, object> d) : base(d) {
			times = t;
		}
	}
}