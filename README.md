# UnitOfWork.Lib

TODO: mô tả thư viện.

## Cài đặt

Package được publish lên GitHub Packages. Thêm `nuget.config` vào project dùng thư viện:

```xml
<configuration>
  <packageSources>
    <add key="github" value="https://nuget.pkg.github.com/htilssu/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="htilssu" />
      <add key="ClearTextPassword" value="%GITHUB_PACKAGES_TOKEN%" />
    </github>
  </packageSourceCredentials>
</configuration>
```

`GITHUB_PACKAGES_TOKEN` là Personal Access Token (classic) có quyền `read:packages`.

```bash
dotnet add package UnitOfWork.Lib
```

## Phát triển

```bash
dotnet build UnitOfWork.Lib.slnx
dotnet test UnitOfWork.Lib.slnx
dotnet pack UnitOfWork.Lib.slnx -c Release -o artifacts
```

## Phát hành

Version được [MinVer](https://github.com/adamralph/minver) tính từ git tag — không sửa version trong csproj.

| Trạng thái git | Version |
|---|---|
| Commit có tag `v1.2.3` | `1.2.3` |
| 3 commit sau `v1.2.3` | `1.2.4-alpha.0.3` |
| Chưa có tag | `0.1.0-alpha.0.<số commit>` |

```bash
git tag v0.1.0
git push origin v0.1.0
```

CI ([`.github/workflows/publish.yml`](.github/workflows/publish.yml)) dùng workflow dùng chung [`htilssu/dotnet-workflows`](https://github.com/htilssu/dotnet-workflows): pull request chỉ build + test; push lên `main` publish bản prerelease; push tag `v*.*.*` publish bản release.
