/*
Copyright (c) 2026 Ethan J. Musser

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System.IO;
using System.Text;
using System.Xml;

namespace SW2RD.MJCF
{
    // Lightweight wrapper around XmlWriter that mirrors the URDFWriter pattern.
    // Owns no state of its own beyond the underlying writer: callers write each
    // element by invoking WriteMJCF on the data classes directly.
    internal class MJCFWriter
    {
        public readonly string SaveLocation;
        public XmlWriter writer;

        public MJCFWriter(string saveLocation)
        {
            SaveLocation = saveLocation;
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineOnAttributes = false,
                IndentChars = "  ",
            };
            writer = XmlWriter.Create(saveLocation, settings);
        }

        public void Close()
        {
            writer.Flush();
            writer.Close();
        }
    }
}
