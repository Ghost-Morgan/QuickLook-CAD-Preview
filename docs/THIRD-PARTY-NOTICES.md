# Third-party notices

The plugin source is licensed under GPL-3.0-or-later. The following
third-party components are used by the build or runtime:

## ACadSharp 3.8.30

Used to read DWG and DXF documents and expose CAD and proxy geometry.

- Project: https://github.com/DomCR/ACadSharp
- License: MIT
- Copyright: Copyright (c) 2021 Albert Domenech

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
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Costura.Fody 6.0.0

Used to embed managed runtime dependencies in the Release plugin assembly.

- Project: https://github.com/Fody/Costura
- License: MIT

## Microsoft package dependencies

ACadSharp's .NET Standard 2.0 target depends on `System.Memory` 4.6.3 and
`System.Text.Encoding.CodePages` 9.0.6.

- Project: https://github.com/dotnet/runtime
- License: MIT

The release pack script includes only the plugin assembly and QuickLook
metadata; it does not include the former commercial CADImport engine.
