using System.Collections.Generic;

namespace smps2asm {
	internal class Condition {
		public Dic True { get; }
		public Dic False { get; }
		private string condition;
		

		public Condition(string c, Dictionary<string, object> t, Dictionary<string, object> f) {
			True = new Dic(t);
			False = new Dic(f);
			condition = c;
		}

		public string GetCondition() {
			return condition;
		}
	}
}