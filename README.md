[![AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
# Ollama GUI Front End - v0.2.1 Alpha

## What is this?
I used WPF to make a front-end application for Ollama. It's not too good but, it is what it is ¯\\_(ツ)\_/¯. 
Make sure to **install Ollama** before using this application. You can install ollama for windows [here](https://ollama.com/download/OllamaSetup.exe).

##  *Please note this is in a early alpha stage, the base work is barely laid out!* 
Shoutout to [OllamaSharp](https://github.com/awaescher/OllamaSharp) this package helped make this app actually work.

## Upcoming features
- Nicer formatting for deep thinking models
- Models tab for downloading and browsing ollama's listed models from the app
- Support for vision models and uploading files into the chat
- Tab for multiple saved chats in the app

## Compiling instructions
1. Clone this repository.
2. Open **Visual Studio 2022** (or **Blend for Visual Studio** - recommended).
3. Ensure you have the **.NET Desktop Development** workload installed.
4. Open the `.sln` file and run/build the project.
5. If any errors occur, report them [here](https://github.com/Kos256/Ollama-GUI-Front-End/issues).

NOTE: This App only works on __Windows__ as WPF is a .NET and Windows dependent GUI framework.
If you’re on Linux or macOS… go ahead. fork this repo, you're doing us a favor haha.
For Linux, you could use [Wine](https://www.winehq.org/) and make sure to get [.NET](https://learn.microsoft.com/en-us/dotnet/core/install/linux) if you want to build the project.

## License
This project is licensed under the **AGPL v3**. See the [full license](https://www.gnu.org/licenses/agpl-3.0).