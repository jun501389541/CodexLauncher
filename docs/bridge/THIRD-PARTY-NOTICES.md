# Third-party notices

This manifest covers the locked NuGet dependency graph and the self-contained Windows frameworks used by the application as of 2026-10-03. The authoritative NuGet versions and hashes are in `CodexLauncher.App/packages.lock.json` and `CodexLauncher.Core/packages.lock.json`. .NET single-file publish did not place the framework notice files beside the executable, so version-matched .NET 10.0.11 runtime and Windows Forms notices are included under `licenses/`.

The matching license texts are included in the `licenses/` directory next to this file. Package source URLs are provided for attribution and version provenance.

| Package | Version | License | Attribution / source |
| --- | --- | --- | --- |
| Makaretu.Dns.Multicast | 0.27.0 | MIT | Richard Schneider; [source and license](https://github.com/richardschneider/net-mdns) |
| QRCoder | 1.8.0 | MIT | Raffael Herrmann and Shane Krueger; [source](https://github.com/Shane32/QRCoder) |
| Common.Logging | 3.4.1 | Apache-2.0 | Common.Logging contributors; [source and license](https://github.com/net-commons/common-logging) |
| Common.Logging.Core | 3.4.1 | Apache-2.0 | Common.Logging contributors; [source and license](https://github.com/net-commons/common-logging) |
| IPNetwork2 | 2.1.2 | BSD-2-Clause | IPNetwork contributors; [source and license](https://github.com/lduchosal/ipnetwork/blob/master/LICENSE) |
| Makaretu.Dns | 2.0.1 | MIT | Richard Schneider; [source and license](https://github.com/richardschneider/net-dns) |
| Microsoft.Data.Sqlite | 10.0.12 | MIT | Microsoft Corporation; [source](https://github.com/dotnet/efcore) |
| Microsoft.Data.Sqlite.Core | 10.0.12 | MIT | Microsoft Corporation; [source](https://github.com/dotnet/efcore) |
| Microsoft.NETCore.Platforms | 1.1.0 | Microsoft .NET Library License Terms | Microsoft Corporation; [package metadata](https://www.nuget.org/packages/Microsoft.NETCore.Platforms/1.1.0) |
| NETStandard.Library | 1.6.1 | Microsoft .NET Library License Terms | Microsoft Corporation; [package metadata](https://www.nuget.org/packages/NETStandard.Library/1.6.1) |
| Microsoft.NETCore.App runtime | 10.0.11 | MIT | .NET Foundation and contributors; [source](https://github.com/dotnet/runtime/tree/v10.0.11) |
| Microsoft.WindowsDesktop.App runtime | 10.0.11 | MIT | .NET Foundation and contributors; [source](https://github.com/dotnet/winforms/tree/v10.0.11) |
| Microsoft.AspNetCore.App runtime | 10.0.11 | Apache-2.0 | .NET Foundation; its third-party notice is included in the .NET Runtime notice file; [source](https://github.com/dotnet/aspnetcore/tree/v10.0.11) |
| SimpleBase | 1.3.1 | Apache-2.0 | Sedat Kapanoglu; [source](https://github.com/ssg/SimpleBase) |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | Apache-2.0 | SourceGear, LLC; [source and license](https://github.com/ericsink/SQLitePCL.raw) |
| SQLitePCLRaw.core | 2.1.12 | Apache-2.0 | SourceGear, LLC; [source and license](https://github.com/ericsink/SQLitePCL.raw) |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | Apache-2.0 | SourceGear, LLC; [source and license](https://github.com/ericsink/SQLitePCL.raw). The bundled SQLite engine is public domain; [SQLite copyright](https://sqlite.org/copyright.html). |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.12 | Apache-2.0 | SourceGear, LLC; [source and license](https://github.com/ericsink/SQLitePCL.raw) |
| Tmds.LibC | 0.2.0 | MIT | Tom Deseyn; [source and license](https://github.com/tmds/Tmds.LibC) |

`Microsoft.NETCore.Platforms` and `NETStandard.Library` are compatibility/reference packages in the resolved graph. The self-contained publish carries the .NET 10 Windows Desktop, ASP.NET Core, and .NET Core runtimes. The tested single-file publish did not place the corresponding framework notices beside the executable, so the version-matched notices are included manually in `licenses/`. Runtime inclusion is determined by the publish output, not by this NuGet graph alone.

## License texts

- [Apache License 2.0](licenses/Apache-2.0.txt)
- [MIT License](licenses/MIT.txt)
- [BSD 2-Clause License](licenses/BSD-2-Clause.txt)
- [Microsoft .NET Library License Terms](licenses/Microsoft.NET-Library-License-Terms.txt)
- [.NET Runtime 10.0.11 MIT license](licenses/dotnet-runtime-10.0.11-MIT.txt)
- [.NET Runtime 10.0.11 third-party notices](licenses/dotnet-runtime-10.0.11-THIRD-PARTY-NOTICES.txt)
- [.NET Windows Forms 10.0.11 third-party notices](licenses/dotnet-winforms-10.0.11-THIRD-PARTY-NOTICES.txt)

This inventory is a package and notice record, not legal advice. Rebuild this list whenever locked package versions or the publish runtime change.
