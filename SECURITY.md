# Security Policy

## Reporting Security Issues

If you discover a security issue in this personal project, please open a GitHub issue or submit a pull request with a fix.

## Supported Versions

Currently supporting the latest version only.

## Security Best Practices

When using this project:

### API Keys and Secrets
- ? **NEVER** commit `.env` files to Git
- ? Use environment variables in production
- ? Rotate API keys regularly
- ? Use secret management systems (e.g., Azure Key Vault, AWS Secrets Manager)

### Mailgun Configuration
- ? Use authorized recipients list for sandbox domains
- ? Validate email addresses before sending
- ? Consider rate limiting in production

### OpenAI API
- ? Monitor API usage and costs
- ? Set spending limits in OpenAI dashboard
- ? Use least-privilege API keys

### Data Storage
- ? The `/data/state.json` file may contain sensitive information
- ? Ensure proper file permissions in production
- ? Consider encryption for sensitive data

### Dependencies
- ? Regularly update NuGet packages
- ? Monitor for security advisories
- ? Use `dotnet list package --vulnerable` to check for vulnerabilities

## Known Security Considerations

1. **Price Scraping**: The `NaivePriceFetcher` uses simple web scraping which may expose you to malicious content. Consider using official APIs when available.

2. **RSS Feeds**: Untrusted RSS feeds could contain malicious content. The current implementation has basic error handling but should be enhanced for production use.

3. **State File**: The `state.json` file is stored in plain text. Consider encryption if it contains sensitive information.

## Recommended Production Hardening

- [ ] Use HTTPS for all API calls (already default)
- [ ] Implement request timeouts and retries
- [ ] Add input validation and sanitization
- [ ] Use structured logging with sanitization
- [ ] Implement rate limiting for external APIs
- [ ] Set up monitoring and alerting
- [ ] Use containerization with minimal base images
- [ ] Run with least-privilege user accounts

## Dependency Security

Regular security checks:
```bash
dotnet list package --vulnerable
dotnet list package --outdated
```

Thank you for helping keep this project secure!
