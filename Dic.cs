using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace smps2asm {
	class Dic {
		private object dic;

		public Dic(object d) {
			Type t = d.GetType();
			if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) {
				dic = d;

			} else {
				Program.error("Object '" + d + "' is not a Dictionary!");
			}
		}

		public object GetDictionary() {
			return dic;
		}

		private Type Type() {
			return dic.GetType();
		}

		public Type ValueType() {
			return Type().GetGenericArguments()[1];
		}

		private object Invoke(string method, object[] param) {
			try {
				if (param != null) {
					Type[] types = new Type[param.Length];
					for (int i = 0;i < param.Length;i++) {
						types[i] = param[i].GetType();
					}

					return Type().GetMethod(method, types).Invoke(dic, param);

				} else {
					return Type().GetMethod(method).Invoke(dic, null);
				}

			} catch (Exception e) {
				Program.error("Dictionary threw an exception for method '" + method + "': ", e);
				return null;
			}
		}

		public int Count() {
			return (int) dic.GetType().GetProperty("Count").GetValue(dic, null);
		}

		public bool ContainsKey(string key) {
			return (bool) Invoke("ContainsKey", new object[] { key });
		}

		public bool ContainsValue(object val) {
			return (bool) Invoke("ContainsValue", new object[] { val });
		}

		public object GetValue(string key) {
			foreach(KeyValuePair<string, object> kv in GetKeyset()) {
				if (kv.Key.Equals(key)) {
					return kv.Value;
				}
			}

			return null;
		}

		public void SetValue(string key, object val) {
			Remove(key);
			Add(key, val);
		}

		public KeyValuePair<string, object>[] GetKeyset() {
			KeyValuePair<string, object>[] set = new KeyValuePair<string, object>[Count()];
			Dictionary<string, object>.KeyCollection keys = (Dictionary<string, object>.KeyCollection) dic.GetType().GetProperty("Keys").GetValue(dic, null);
			Dictionary<string, object>.ValueCollection vals = (Dictionary<string, object>.ValueCollection) dic.GetType().GetProperty("Values").GetValue(dic, null);

			for (int i = 0;i < set.Length;i++) {
				set[i] = new KeyValuePair<string, object>(keys.ElementAt(i), vals.ElementAt(i));
			}

			return set;
		}

		public void Add(string key, object val) {
			Invoke("Add", new object[] { key, val });
		}

		public bool Remove(string key) {
			return (bool)Invoke("Remove", new object[] { key });
		}
	}
}
