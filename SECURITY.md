# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub's **Report a vulnerability** button on this repository's **Security** tab. Don't open a public issue.

Include what you found, how to reproduce it, and the EFDoctor version (`efdoctor --version`). Once a fix is released, the report is credited unless you prefer otherwise.

Only the latest released version receives fixes.

## Trust boundary

EFDoctor makes no network requests and sends nothing anywhere. But loading a project means running its MSBuild logic: design-time targets and props authored by the project are executed on your machine, just as they would be by `dotnet build`. **Only analyze repositories you trust.** Code execution through a malicious project file is expected behavior, not a vulnerability in EFDoctor.

In scope, for example:

- EFDoctor sending data off the machine;
- EFDoctor writing to or modifying the analyzed repository;
- a crafted source file that makes EFDoctor execute code outside the MSBuild evaluation of the analyzed project.
