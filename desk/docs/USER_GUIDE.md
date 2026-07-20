# MorphDB Desk - User Guide

MorphDB Desk is a desktop application for managing MorphDB database connections, exploring tables, and performing data operations.

## Table of Contents

1. [Getting Started](#getting-started)
2. [Connections](#connections)
3. [Explorer](#explorer)
4. [Navigation](#navigation)
5. [Command Palette](#command-palette)
6. [Settings](#settings)
7. [Keyboard Shortcuts](#keyboard-shortcuts)
8. [Troubleshooting](#troubleshooting)

---

## Getting Started

### First Launch

When you launch MorphDB Desk for the first time, a connection dialog will automatically appear prompting you to set up your first database connection.

### System Requirements

- Windows 10/11, macOS 10.15+, or Linux
- At least 4GB RAM recommended
- Network access to MorphDB server

---

## Connections

### Adding a New Connection

1. Click the **+** button in the sidebar, or use `Cmd/Ctrl + Shift + N`
2. Fill in the connection details:
   - **Name**: A friendly name for this connection
   - **Host**: Server hostname or IP address (default: `localhost`)
   - **Port**: Server port (default: `5000`)
   - **Project ID**: Your project identifier
   - **API Key** (optional): Authentication key if required
3. Click **Test Connection** to verify settings
4. Click **Save** to add the connection

### Managing Connections

- **Edit**: Right-click a connection and select "Edit"
- **Delete**: Right-click a connection and select "Delete" (requires confirmation)
- **Test**: Right-click and select "Test Connection" to verify connectivity
- **Connect/Disconnect**: Click to toggle connection state

### Connection Status Indicators

| Indicator | Status |
|-----------|--------|
| Gray dot | Disconnected |
| Yellow dot (pulsing) | Connecting |
| Green dot | Connected |
| Red dot | Error |

---

## Explorer

The Explorer is the main workspace for viewing and managing your database tables.

### Table Tree

- Located on the left side of the Explorer
- Shows all tables in the connected database
- Click a table name to view its data

### Table View

- Displays table data in a grid format
- Supports sorting by clicking column headers
- Supports resizing columns by dragging borders

### Data Operations

*(Coming in future versions)*

- Create, Read, Update, Delete (CRUD) operations
- Filtering and advanced search
- Export data to various formats

---

## Navigation

### Sidebar

The sidebar provides quick access to all major sections:

| Section | Description |
|---------|-------------|
| **Explorer** | Table data browser |
| **Projects** | Project management |
| **Views** | Saved data views |
| **Webhooks** | Webhook configuration |
| **Audit Logs** | Activity logging |
| **Security** | Security settings |
| **Settings** | Application settings |

### Collapsible Sidebar

- Toggle sidebar with the collapse button (top right of sidebar)
- Or use keyboard shortcut: `Cmd/Ctrl + B`
- When collapsed, hover over icons to see tooltips

### Quick Navigation

Use `Cmd/Ctrl + 1` through `Cmd/Ctrl + 6` to jump directly to sections:

| Shortcut | Section |
|----------|---------|
| `Cmd/Ctrl + 1` | Explorer |
| `Cmd/Ctrl + 2` | Projects |
| `Cmd/Ctrl + 3` | Views |
| `Cmd/Ctrl + 4` | Webhooks |
| `Cmd/Ctrl + 5` | Audit Logs |
| `Cmd/Ctrl + 6` | Settings |

---

## Command Palette

Access the command palette with `Cmd/Ctrl + K`.

### Features

- **Quick Navigation**: Type a page name to navigate instantly
- **Theme Control**: Search for "theme" to toggle or set themes
- **Connection Switching**: Search for connection names

### Tips

- Start typing immediately after opening
- Use arrow keys to navigate results
- Press Enter to execute selected action
- Press Escape to close

---

## Settings

### General Settings

*(Coming in future versions)*

- Default connection preferences
- Application behavior settings

### Appearance

- **Theme**: Choose between Light, Dark, or System theme
- Toggle theme quickly with `Cmd/Ctrl + Shift + T`

### Data Management

*(Coming in future versions)*

- Import/Export preferences
- Data display formats

---

## Keyboard Shortcuts

### Quick Reference

| Category | Shortcut | Action |
|----------|----------|--------|
| **Global** | `Cmd/Ctrl + K` | Open Command Palette |
| | `Cmd/Ctrl + B` | Toggle Sidebar |
| | `Cmd/Ctrl + Shift + N` | New Connection |
| | `Cmd/Ctrl + Shift + T` | Toggle Theme |
| | `Cmd/Ctrl + Shift + R` | Reload Window |
| | `?` | Show Shortcuts Help |
| | `Esc` | Close dialogs |
| **Navigation** | `Cmd/Ctrl + 1-8` | Jump to section |

For complete shortcuts reference, see [KEYBOARD_SHORTCUTS.md](./KEYBOARD_SHORTCUTS.md).

---

## Troubleshooting

### Connection Issues

**Problem**: Cannot connect to server
- Verify the server is running and accessible
- Check hostname, port, and network connectivity
- Ensure firewall allows the connection
- Verify API key if authentication is required

**Problem**: Connection times out
- Check network stability
- Verify server is responding
- Try increasing timeout in settings (if available)

### Display Issues

**Problem**: UI appears broken
- Try reloading the window (`Cmd/Ctrl + Shift + R`)
- Clear application cache (Settings > Clear Cache)
- Update to the latest version

### Performance Issues

**Problem**: Application is slow
- Close unused connections
- Limit table data queries
- Check system resource usage
- Restart the application

### Error Messages

When encountering errors:
1. Note the exact error message
2. Check if the action can be retried
3. In development mode, expand error details for stack trace
4. Copy error details for bug reports

---

## Getting Help

- **Documentation**: See `/docs` folder for technical documentation
- **Keyboard Shortcuts**: Press `?` for quick reference
- **Bug Reports**: [GitHub Issues](https://github.com/iyulab/MorphDB/issues)

---

*Version: 0.14.0 - Testing & UX Foundations*
