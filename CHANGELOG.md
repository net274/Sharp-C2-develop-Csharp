# Changelog
All notable changes to this project will be documented in this file.

## [0.2.3] - 2021-08-13
### Added
- Reverse port forwards.

## [0.2.2] - 2021-08-13
### Added
- Token commands. Credit to [@MDSecLabs](https://twitter.com/MDSecLabs) for their idea of a "token store".

## [0.2.1] - 2021-08-13
### Added
- Null-reference check to the Handler string when generating payloads.

## [0.2.0] - 2021-08-09
### Changed
- Moved payload generation to its own API endpoint.
- Exposed additional payload option for DllExport name.
### Added
- New Client screen for payload generation.

## [0.1.0] - 2021-08-07
### Added
- New Handler API endpoint to load a Handler on the Team Server during runtime.
- New Client command on the `handlers` screen to call said API.
### Changed
- Put authentication on the SignalR hub.

## [0.0.2] - 2021-08-06
### Changed
- Fix bug when starting the default HTTP Handler.

## [0.0.1] - 2021-08-06
### Added
- First release.