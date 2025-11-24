# Quick Start Guide for GitHub

## Initial Setup (First Time Only)

### 1. Initialize Git Repository
```bash
cd C:\Dev\Active\ai-newsletter
git init
```

### 2. Create GitHub Repository
- Go to https://github.com/new
- Name: `ai-newsletter`
- Description: "AI-powered personalized news digest generator"
- **DO NOT** initialize with README (you already have one)
- Click "Create repository"

### 3. Connect Local to GitHub
```bash
# Add remote (replace 'yourusername' with your GitHub username)
git remote add origin https://github.com/yourusername/ai-newsletter.git

# Verify remote
git remote -v
```

### 4. Stage and Commit Files
```bash
# Check what will be committed (make sure .env is NOT listed!)
git status

# Add all files
git add .

# Commit
git commit -m "Initial commit: AI Newsletter project"
```

### 5. Push to GitHub
```bash
# Push to main branch
git branch -M main
git push -u origin main
```

## Important: Verify No Secrets Were Committed

Before pushing, double-check:
```bash
# Make sure .env is NOT tracked
git status

# If you see .env listed, run:
git rm --cached .env
git commit -m "Remove .env from tracking"
```

## Daily Workflow

### Making Changes
```bash
# Check current status
git status

# Stage specific files
git add filename.cs

# Or stage all changes
git add .

# Commit with message
git commit -m "Description of changes"

# Push to GitHub
git push
```

### Creating Feature Branches
```bash
# Create and switch to new branch
git checkout -b feature/new-feature

# Make changes and commit
git add .
git commit -m "Add new feature"

# Push branch to GitHub
git push -u origin feature/new-feature

# Then create Pull Request on GitHub
```

## Useful Commands

### Check Status
```bash
git status                  # See changed files
git log --oneline          # View commit history
git diff                   # See uncommitted changes
```

### Undo Changes
```bash
git checkout -- filename   # Discard changes to file
git reset HEAD filename    # Unstage file
git reset --hard HEAD      # Discard all changes (dangerous!)
```

### Update from GitHub
```bash
git pull                   # Get latest changes
```

## GitHub Features to Enable

1. **Branch Protection** (Settings ? Branches)
   - Protect `main` branch
   - Require pull request reviews
   - Require status checks to pass

2. **GitHub Actions** (Already configured!)
   - Builds will run automatically on push

3. **Dependabot** (Settings ? Security)
   - Enable dependency updates
   - Get alerts for vulnerable packages

4. **Secrets** (Settings ? Secrets and variables ? Actions)
   - Add secrets for CI/CD if needed
   - Never commit secrets to code

## Troubleshooting

### If you accidentally committed .env:
```bash
# Remove from Git but keep local file
git rm --cached .env
git commit -m "Remove .env from repository"
git push

# Then add to .gitignore (already done!)
```

### If you need to remove sensitive data from history:
Use GitHub's guide: https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository

Or use BFG Repo-Cleaner: https://rtyley.github.io/bfg-repo-cleaner/

## Next Steps

1. ? Verify build passes locally: `dotnet build`
2. ? Double-check .env is not tracked: `git status`
3. ? Push to GitHub
4. ? Add repository description and topics on GitHub
5. ? Enable GitHub Actions
6. ? Set up branch protection rules
7. ? Add collaborators if needed
8. ? Share your project!

## Resources

- [GitHub Documentation](https://docs.github.com)
- [Git Cheat Sheet](https://education.github.com/git-cheat-sheet-education.pdf)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)

Happy coding! ??
