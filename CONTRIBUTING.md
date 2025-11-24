# Contributing to AI Newsletter

Thank you for your interest in contributing! Here are some guidelines to help you get started.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/yourusername/ai-newsletter.git`
3. Create a feature branch: `git checkout -b feature/your-feature-name`
4. Make your changes
5. Test thoroughly
6. Commit with clear messages: `git commit -m "Add feature: description"`
7. Push to your fork: `git push origin feature/your-feature-name`
8. Open a Pull Request

## Development Setup

1. Install .NET 10 SDK
2. Copy `.env.example` to `.env` and configure with test credentials
3. Run `dotnet restore` to install dependencies
4. Run `dotnet build` to verify everything compiles
5. Run `dotnet run` to test locally

## Code Style

- Follow standard C# conventions
- Use meaningful variable names
- Add comments for complex logic
- Keep methods focused and concise
- Use async/await for I/O operations

## Testing

- Test your changes thoroughly before submitting
- Ensure the build passes: `dotnet build`
- Verify no secrets are committed

## Pull Request Guidelines

- Keep PRs focused on a single feature or fix
- Update README.md if adding new features
- Describe your changes clearly in the PR description
- Link any related issues

## Reporting Issues

- Use GitHub Issues
- Provide clear reproduction steps
- Include error messages and logs (sanitize any secrets!)
- Specify your environment (.NET version, OS, etc.)

## Feature Requests

We welcome feature requests! Please:
- Check if it's already been requested
- Describe the use case clearly
- Explain why it would benefit users

## Questions?

Feel free to open a discussion or issue if you have questions!

Thank you for contributing! ??
