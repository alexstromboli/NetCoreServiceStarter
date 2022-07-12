using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Utils.Args
{
	internal class Args : List<string>
	{
		protected string KeyPrefixRegexPattern;

		public Args (string[] args, bool LinuxStyle = false) :
			base (args)
		{
			KeyPrefixRegexPattern = LinuxStyle
				? "--"
				: "[-/]";
		}

		public int FindFirstMatch (string strPattern)
		{
			Regex re = new Regex (strPattern);

			int i = 0;
			foreach (string strArg in this)
			{
				if (re.Match (strArg).Success)
				{
					return i;
				}

				++i;
			}

			return -1;
		}

		public string ExcludeFirstMatch (string strPattern)
		{
			int nPos = FindFirstMatch (strPattern);
			if (nPos == -1)
			{
				return null;
			}

			string strRes = this[nPos];
			RemoveAt (nPos);

			return strRes;
		}

		// case insensitive
		public string GetAndExcludeParam (string strKeyBody)
		{
			int nPos = FindFirstMatch (@"(?i)\A" + KeyPrefixRegexPattern + Regex.Escape(strKeyBody) + @"\Z");
			if (nPos == -1)
			{
				return null;
			}

			string res = nPos <= Count - 2 ? this[nPos + 1] : null;
			RemoveAt (nPos);
			RemoveAt (nPos);

			return res;
		}

		// case insensitive
		public string GetAndExcludeKey (string strKeyBody)
		{
			int nPos = FindFirstMatch (@"(?i)\A" + KeyPrefixRegexPattern + Regex.Escape (strKeyBody) + @"\Z");
			if (nPos == -1)
			{
				return null;
			}

			string res = this[nPos];
			RemoveAt (nPos);

			return res;
		}
	}
}
