using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DemoROVDashboard
{
    class Value : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public static string START = "~";
        public static string SEPARATOR = "-";
        public static string[] TYPE = { "I", "F", "S" };
        public static string END = "\n";
        public string Key { get; set; }
        public ValueType Type = ValueType.INT;
        public int TypeInt { get { return (int)Type; } set { Type = (ValueType)value; } }
        public int ValueInt = 0;
        public double ValueFloat = 0.0;
        public string ValueStr = string.Empty;
        public string ValueRepr
        {
            get
            {
                switch (Type)
                {
                    case ValueType.INT:
                        return ValueInt.ToString();
                    case ValueType.FLOAT:
                        return ValueFloat.ToString();
                }
                return ValueStr;
            }
            set
            {
                switch (Type)
                {
                    case ValueType.INT:
                        try
                        {
                            Match match = Regex.Match(value, @"^\d+");
                            if (match.Success) ValueInt = int.Parse(match.Value);
                        }
                        catch { ValueInt = 0; }
                        break;
                    case ValueType.FLOAT:
                        try
                        {
                            Match match = Regex.Match(value, @"^\d+.\d+");
                            if (match.Success) ValueFloat = double.Parse(match.Value);
                        }
                        catch { ValueFloat = 0.0; }
                        break;
                    default:
                        ValueStr = value;
                        break;
                }
                NotifyPropertyChanged();
            }
        }

        public string Serialize()
        {
            string serializedStr = string.Empty;
            serializedStr += START;
            serializedStr += Key;
            serializedStr += SEPARATOR;
            serializedStr += TYPE[(int)Type];
            serializedStr += SEPARATOR;
            serializedStr += ValueRepr;
            serializedStr += END;
            return serializedStr;
        }

        public static explicit operator int(Value a)
        {
            return a.ValueInt;
        }

        public static explicit operator double(Value a)
        {
            return a.ValueInt;
        }
        public static explicit operator string(Value a)
        {
            return a.ValueStr;
        }

        private void NotifyPropertyChanged(String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }



    public enum ValueType
    {
        INT,
        FLOAT,
        STRING
    }
}
