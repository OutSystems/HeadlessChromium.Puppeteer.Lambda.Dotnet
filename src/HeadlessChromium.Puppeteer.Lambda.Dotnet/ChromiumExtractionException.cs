using System;
using System.Collections.Generic;
using System.Text;

namespace HeadlessChromium.Puppeteer.Lambda.Dotnet 
 {
    public class ChromiumExtractionException : Exception 
    {
        public ChromiumExtractionException(string message) : base(message) 
        {
        }

        public ChromiumExtractionException(string message, Exception inner) : base(message, inner) 
        {
        }
    }
}
