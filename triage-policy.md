# Issue Triage Policy

This policy aims to ensure that issues are handled efficiently and transparently, keeping the project on track and maintaining a high level of community engagement.

For reporting security vulnerabilities, follow [this guidance](SECURITY.md).

## Categorize Issues

* **Bugs**:  issues that describe a malfunction or unintended behavior
* **Feature Requests**:  suggestions for new features or enhancements
* **Documentation**:  issues related to missing or unclear documentation
* **Questions**:  general inquiries or requests for clarification

## Prioritize Issues

* **Priority 0 (P0)**:  cannot release without addressing
* **Priority 1 (P1)**:  blocking core scenarios, regressions, or high-impact issues affecting many users
* **Priority 2 (P2)**:  important but not blocking, such as feature requests with significant community interest
* **Priority 3 (P3)**:  low-impact issues, minor bugs, or enhancements with limited scope

## Label Issues

* Use labels to indicate the type and priority of the issue (e.g.:  [bug](https://github.com/dotnet/sign/labels/bug), [feature-request](https://github.com/dotnet/sign/labels/feature-request), [Priority:1](https://github.com/dotnet/sign/labels/Priority%3A1), [Priority:2](https://github.com/dotnet/sign/labels/Priority%3A2)).
* Additional labels can be used for specific areas of the project (e.g., CLI, documentation).

## Review and Update

* Conduct triage meetings (typically weekly, except in December) to review new issues and update the status of existing ones.
* If more information is needed from the community, the issue will be labelled with [needs-more-info](https://github.com/dotnet/sign/labels/needs-more-info) and awaited.
* If information is needed from the issue author and the author has not responded within 14 days, the issue will be closed but can be reactivated when information is available.

## Triage Outcome

A triaged issue should fall into one of these states:

* a priority has been assigned and the issue is in the backlog
* the issue is labeled with needs-more-info and is waiting on user response
* the issue is closed (e.g.:  question immediately answered)
