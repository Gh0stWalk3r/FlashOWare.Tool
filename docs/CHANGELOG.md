# FlashOWare.Tool
Release Changelog

[goto Prerelease_Changelog;](./CHANGELOG-Prerelease.md)

## [vNext]
### Tool
- **Added** `flashoware` root command (supports cancellation)
  - with `-!|-a|--about` option to get application information
  - with `-#|-i|--info` option to get environment information
  - with `--version` option to get version information
  - with `-?|-h|--help` option to get help and usage information
- **Added** `flashoware using` command
  - with `-?|-h|--help` option to get help and usage information
- **Added** `flashoware using count` command to count and list top-level using directives
  - with `<USINGS>` arguments (optional) to set the names of top-level using directives to count and list
    - if not specified, counts and lists all top-level using directives
  - with `--proj|--project <project>` option to set the project file to operate on
    - if not specified, searches the current working directory for a single project file
  - with `-?|-h|--help` option to get help and usage information
- **Added** `flashoware using globalize` command to change top-level using directives to global using directives
  - with `<USINGS>` arguments (optional) to set the names of top-level using directives to convert to global using directives
    - if not specified, globalizes all top-level using directives
  - with `--proj|--project <project>` option to set the project file to operate on
    - if not specified, searches the current working directory for a single project file
  - with `--force` option to enable globalizing all top-level using directives when no `<USINGS>` arguments are specified
  - with `-?|-h|--help` option to get help and usage information

### Package
- **Added** .NET tool targeting _.NET 6.0_ and _.NET 7.0_.

[vnext]: https://github.com/FlashOWare/FlashOWare.Tool/commits/main
