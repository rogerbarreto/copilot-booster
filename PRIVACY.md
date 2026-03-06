# Privacy Policy

**Copilot Booster** — Last updated: March 6, 2026

Roger Barreto ("I") built Copilot Booster as an open-source application licensed under the MIT License. This privacy policy explains how your information is handled when you use the application.

## Your Privacy

I take your privacy seriously. Copilot Booster **does not collect, transmit, or share any personal information**. The application does not include any telemetry, analytics, or crash reporting.

## Information Stored Locally

All data created by the application remains on your local machine and is never sent to any external server. This includes:

- **Application settings** — user preferences such as allowed tools, allowed directories, IDE configurations, and UI options stored in your local `AppData` folder.
- **Session references** — working directory paths, session identifiers, and aliases for your Copilot CLI sessions.
- **Window tracking cache** — a local file that remembers which IDE, Explorer, Edge, and Teams windows are associated with your sessions so they can be restored after an app restart.
- **Log files** — diagnostic logs stored locally for troubleshooting. These contain only operational information (events, errors) and no personal data.

## Network Connections

Copilot Booster makes the following outbound network requests:

- **Update check** — queries the GitHub Releases API (`api.github.com`) to check for newer versions of the application.
- **Teams icon** — downloads the Microsoft Teams favicon from `teams.microsoft.com` for display purposes (cached locally for 7 days).

No personal data is included in these requests beyond standard HTTP metadata (IP address, user agent).

The application does **not** use cookies, tracking pixels, analytics services, or advertising networks.

## Third-Party Services

Copilot Booster launches and manages **GitHub Copilot CLI** sessions. Your interactions with Copilot CLI are governed by [GitHub's Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

When you open Microsoft Teams through the application, it opens the Teams web app in Microsoft Edge, which is governed by [Microsoft's Privacy Statement](https://privacy.microsoft.com/en-us/privacystatement).

I have no access to or control over the data handled by these third-party services.

## Links to Third-Party Websites

The application may contain links to external sites. I am not responsible for the privacy practices of those websites. You should review their individual privacy policies.

## Security

All application data is stored locally on your machine using standard file system storage. No data is transmitted to external servers under my control. While I strive to follow secure coding practices, no software can guarantee absolute security.

## Children's Privacy

Copilot Booster is a software development tool not directed at children under 13 years of age.

## Changes to This Privacy Policy

This privacy policy is effective as of the "Last updated" date shown above. I reserve the right to update it at any time. Changes will be posted in this repository, and the date above will be revised accordingly.

## Contact

For any questions or concerns regarding this privacy policy, please [open an issue](https://github.com/rogerbarreto/copilot-booster/issues) on the GitHub repository.
