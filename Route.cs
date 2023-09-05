using System;
using System.Collections.Generic;
using System.Text;

namespace IgisBot
{
    public class Route
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public int Sinature { get; set; }
        public int RouteNumber {get;set;}
        public string FirstStop { get; set; }
        public string LastStop { get; set; }
    }
}
