# Security Policy

## Supported Versions

We release patches for security vulnerabilities for the following versions:

| Version | Supported          |
| ------- | ------------------ |
| 1.x.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Reporting a Vulnerability

We take the security of smodr seriously. If you believe you have found a security vulnerability, please report it to us as described below.

### How to Report

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, please report them via GitHub's Security Advisory feature:

1. Go to the [Security tab](https://github.com/cascadiacollections/smodr-winui3/security)
2. Click "Report a vulnerability"
3. Fill out the advisory form with:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if you have one)

Alternatively, you can email security concerns to the repository maintainers.

### What to Include

Please include the following information in your report:

- Type of vulnerability
- Full paths of source file(s) related to the vulnerability
- Location of the affected source code (tag/branch/commit or direct URL)
- Any special configuration required to reproduce the issue
- Step-by-step instructions to reproduce the issue
- Proof-of-concept or exploit code (if possible)
- Impact of the issue, including how an attacker might exploit it

### Response Timeline

- **Initial Response**: Within 48 hours
- **Status Update**: Within 7 days
- **Fix Timeline**: Depends on severity and complexity
  - Critical: Within 7 days
  - High: Within 14 days
  - Medium: Within 30 days
  - Low: Next regular release

### Security Update Process

1. We'll acknowledge your report and begin investigation
2. We'll keep you informed of our progress
3. We'll develop and test a fix
4. We'll release a security update
5. We'll credit you in the security advisory (unless you prefer to remain anonymous)

## Known Security Considerations

### Application Security

- **RSS Feed Parsing**: The app parses external RSS feeds. We validate and sanitize input, but be cautious with untrusted feed sources.
- **Media Playback**: Audio files are loaded from external URLs. Ensure feeds point to trusted sources.
- **Local Storage**: Episode cache and settings are stored locally. Sensitive data should not be stored in the app.
- **Network Communication**: All network requests should use HTTPS when possible.

### Dependencies

We use Dependabot to keep dependencies up to date and automatically create pull requests for security updates.

Our key dependencies include:
- Microsoft.WindowsAppSDK
- CommunityToolkit.Mvvm
- System.ServiceModel.Syndication

### Code Scanning

- **CodeQL**: Automated security scanning runs on every push and pull request
- **Dependency Review**: Automated review of dependency changes in pull requests
- **Secret Scanning**: GitHub scans for accidentally committed secrets

## Security Best Practices for Contributors

When contributing to smodr, please:

1. **Never commit secrets**: API keys, tokens, passwords, etc.
2. **Validate inputs**: Especially data from RSS feeds or user input
3. **Handle exceptions**: Don't expose sensitive information in error messages
4. **Use HTTPS**: For all external communications
5. **Follow OWASP guidelines**: For web-related security concerns
6. **Keep dependencies updated**: Use latest stable versions
7. **Review code**: Look for security issues in code reviews

## Vulnerability Disclosure Policy

- We ask that you give us reasonable time to fix vulnerabilities before public disclosure
- We'll work with you to understand and address the issue
- We'll credit you in our security advisories (if you wish)
- We appreciate responsible disclosure

## Security Features

### Current Protections

- Input validation on RSS feed URLs
- Sanitization of feed content before display
- Exception handling to prevent information disclosure
- No storage of sensitive user data
- CodeQL security scanning in CI/CD

### Planned Improvements

- Enhanced input validation
- Content Security Policy for media sources
- Regular security audits
- Automated dependency scanning

## Contact

For general security questions or concerns, please open a discussion in the GitHub Discussions section or contact the maintainers through GitHub.

## Recognition

We appreciate the security research community and will acknowledge researchers who report valid security issues (unless they prefer to remain anonymous).

---

Thank you for helping keep smodr and its users safe! 🔒
