# Board helpers for the stage 2 walks: tabs and their menu, edit mode, panel tools, the gallery, the panel
# form, pop-outs and boards.json. Dot-source this file; it only defines things.
# ASCII only on purpose: Windows PowerShell 5.1 reads a BOM-less file as ANSI.
. (Join-Path $PSScriptRoot 'uia-import.ps1')

Add-Type -AssemblyName System.Windows.Forms

$script:IsListItem = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::ListItem)

# An element by automation id in any Ur Score window, the windows themselves included (menus and pop-outs are top-level).
function Find-InUrWindows([string]$id) {
    foreach ($w in Get-UrWindows) {
        if ($w.Current.AutomationId -eq $id) { return $w }
        $e = Find-ByAutomationId $w $id
        if ($e) { return $e }
    }
    return $null
}

# Every panel in a window, in board order: "StandingPanel1", "RacePanel1", "AccountCardPanel1PoppedOut".
function Get-PanelIds($root) {
    @($root.FindAll($TS::Descendants, $Cond::TrueCondition) |
        ForEach-Object { $_.Current.AutomationId } |
        Where-Object { $_ -match '^[A-Za-z]+Panel[0-9]+(PoppedOut)?$' })
}

function Get-TabItems($board) {
    $tabs = Find-ByAutomationId $board 'BoardTabs'
    if (-not $tabs) { return @() }
    @($tabs.FindAll($TS::Children, $IsListItem))
}

function Get-TabNames($board) { @(Get-TabItems $board | ForEach-Object { $_.Current.Name }) }

function Get-SelectedTabName($board) {
    $selected = Get-TabItems $board |
        Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } |
        Select-Object -First 1
    if ($selected) { $selected.Current.Name } else { '(none)' }
}

# Selects a tab by its board name, the way a click does.
function Select-Tab($board, [string]$name) {
    $tab = Get-TabItems $board | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1
    if (-not $tab) { throw "no tab '$name'" }
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1000
}

# The accounts table on the board on screen.
function Get-AccountsGrid {
    $panel = Find-ByAutomationId (Get-BoardWindow) 'AccountsTablePanel1'
    if (-not $panel) { return $null }
    Find-ByAutomationId $panel 'AccountsGrid'
}

# A table's column headings in order; a sorted one ends in an arrow.
function Get-GridHeaders($grid) { @(Find-All $grid $CT::HeaderItem | ForEach-Object { $_.Current.Name }) }

# A table's rows in order: named by account, the totals row last.
function Get-GridRows($grid) { @(Find-All $grid $CT::DataItem) }

# Clicks the heading whose name starts with a label, the way a person sorts.
function Invoke-GridHeader($grid, [string]$label) {
    $header = Find-All $grid $CT::HeaderItem | Where-Object { $_.Current.Name.StartsWith($label) } | Select-Object -First 1
    if (-not $header) { throw "no heading '$label'" }
    Invoke-Element $header
    Start-Sleep -Milliseconds 800
}

# Picks a row, the way a click or the arrow keys do.
function Select-GridRow($row) {
    if (-not $row) { throw 'row not found' }
    $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
}

# Picks a row the way a person actually does: keyboard focus lands in it first, then it's selected.
# SelectionItemPattern.Select alone (Select-GridRow) never moves focus, which no click or arrow key leaves
# true -- a caller that wants to assert focus stayed in the table afterward needs this one instead.
# A DataGrid row can't take focus itself; its cells can, so focus goes to the row's first focusable cell.
function Select-GridRowAsUser($row) {
    if (-not $row) { throw 'row not found' }
    $focusable = New-Object System.Windows.Automation.PropertyCondition($AE::IsKeyboardFocusableProperty, $true)
    $cell = $row.FindFirst($TS::Descendants, $focusable)
    if (-not $cell) { throw "no focusable cell in row '$($row.Current.Name)'" }
    $cell.SetFocus()
    Start-Sleep -Milliseconds 300
    $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
}

# The board's own vertical scroll (BoardScroll), 0-100; -1 when it isn't tall enough to scroll at all.
function Get-BoardScrollPercent($board) {
    $scroll = Find-ByAutomationId $board 'BoardScroll'
    if (-not $scroll) { throw 'BoardScroll not found' }
    $pattern = $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if (-not $pattern.Current.VerticallyScrollable) { return -1 }
    $pattern.Current.VerticalScrollPercent
}

# Scrolls the board to a vertical percent (0-100), the way a drag on the scrollbar does.
function Set-BoardScrollPercent($board, [double]$percent) {
    $scroll = Find-ByAutomationId $board 'BoardScroll'
    if (-not $scroll) { throw 'BoardScroll not found' }
    $pattern = $scroll.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    $pattern.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $percent)
    Start-Sleep -Milliseconds 300
}

# Opens the selected tab's right-click menu with Shift+F10 and invokes one item by automation id.
# Returns $false (and closes the menu) when that item is disabled.
function Invoke-TabMenu($board, [string]$itemId) {
    $selected = Get-TabItems $board |
        Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } |
        Select-Object -First 1
    if (-not $selected) { throw 'no tab is selected' }
    $selected.SetFocus()
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('+{F10}')

    $item = $null
    $deadline = (Get-Date).AddSeconds(6)
    do {
        $item = Find-InUrWindows $itemId
        if ($item) { break }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    if (-not $item) { throw "the tab menu has no $itemId" }

    if (-not $item.Current.IsEnabled) {
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 300
        return $false
    }
    Invoke-Element $item
    Start-Sleep -Milliseconds 700
    return $true
}

function Enter-EditMode($board) {
    Invoke-Element (Find-ByAutomationId $board 'EditBoardButton')
    Start-Sleep -Milliseconds 800
}

function Complete-EditMode($board) {
    Invoke-Element (Find-ByAutomationId $board 'DoneButton')
    Start-Sleep -Milliseconds 1000
}

# BC2: the chip opens the status card; Pause and Resume are inside it. The card is a popup, a top-level window of its
# own, so its button is found across the process's windows. Returns the button's new name.
# Unverified as of Task 9 (2026-09-23): the controller ruling for that task forbade launching the app or running a
# walk, so nobody has confirmed by hand yet that Find-InUrWindows (which walks Get-UrWindows) actually reaches into
# a WPF Popup's own HWND the way it reaches a menu or a pop-out window. Treat this as unproven until the first real
# walk-top-bar run: if step 3 ("A click opens the card") times out into the throw below instead of passing, give the
# Popup's Border an AutomationId and walk $AE::RootElement children by process id for it instead, and update this
# comment with whichever one worked.
function Invoke-PauseResume([int]$seconds = 10) {
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'StartStopButton')
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        $button = Find-InUrWindows 'PauseResumeButton'
        if ($button) {
            Invoke-Element $button
            Start-Sleep -Milliseconds 600
            return (Find-InUrWindows 'PauseResumeButton').Current.Name
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw 'the status card never opened'
}

# Invokes a tool button inside one panel, found by the panel's automation id.
function Invoke-PanelTool($root, [string]$panelId, [string]$toolId) {
    $panel = Find-ByAutomationId $root $panelId
    if (-not $panel) { throw "no panel $panelId" }
    $tool = Find-ByAutomationId $panel $toolId
    if (-not $tool) { throw "$panelId shows no $toolId" }
    Invoke-Element $tool
    Start-Sleep -Milliseconds 800
}

# Picks the first item whose name matches a wildcard pattern in a combo box.
function Select-ComboItem($box, [string]$like) {
    if (-not $box) { throw 'combo box not found' }
    $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 500
    $item = Find-All $box $CT::ListItem | Where-Object { $_.Current.Name -like $like } | Select-Object -First 1
    if (-not $item) { throw "no item like '$like'" }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 300
    # Picking can rebuild the panel, which takes the box with it.
    try { $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() } catch { }
    Start-Sleep -Milliseconds 800
}

# Small, Half or Wide in one panel's size box.
# Moving and sizing lost their buttons when the header became the drag target and the edges became the grips,
# so a walk drives them the way a person without a mouse does: focus the panel, then the keys. Focus is taken
# again before every press because each change rebuilds the grid and the element that had focus is destroyed.
# The panel is a UserControl and takes no focus; its first tool does, which is also where the app puts focus back
# after a keyboard move. So the walk focuses that tool and the arrow keys reach the board's handler from there.
function Focus-Panel($board, [string]$panelId) {
    $panel = Find-ByAutomationId $board $panelId
    if (-not $panel) { throw "no panel '$panelId' to focus" }
    $tool = @('PanelSettingsButton', 'RemovePanelButton') | ForEach-Object { Find-ByAutomationId $panel $_ } |
        Where-Object { $_ -and $_.Current.IsKeyboardFocusable } | Select-Object -First 1
    if (-not $tool) { throw "panel '$panelId' shows no tool that takes focus" }
    $tool.SetFocus()
    Start-Sleep -Milliseconds 250
}

function Move-PanelEarlier($board, [string]$panelId) {
    Focus-Panel $board $panelId
    [System.Windows.Forms.SendKeys]::SendWait('{LEFT}')
    Start-Sleep -Milliseconds 500
}

function Set-PanelTall($board, [string]$panelId, [bool]$tall) {
    Focus-Panel $board $panelId
    [System.Windows.Forms.SendKeys]::SendWait($(if ($tall) { '^{DOWN}' } else { '^{UP}' }))
    Start-Sleep -Milliseconds 500
}

# Ctrl+Left and Ctrl+Right step along Small, Half, Wide and stop at the ends, so walking to Small first makes
# the number of steps from there exact whatever the panel started at.
function Set-PanelSize($board, [string]$panelId, [string]$size) {
    $steps = @{ 'Small' = 0; 'Half' = 1; 'Wide' = 2 }[$size]
    if ($null -eq $steps) { throw "unknown size '$size'" }

    foreach ($i in 1..2) {
        Focus-Panel (Get-BoardWindow) $panelId
        [System.Windows.Forms.SendKeys]::SendWait('^{LEFT}')
        Start-Sleep -Milliseconds 400
    }

    if ($steps -eq 0) { return }
    foreach ($i in 1..$steps) {
        Focus-Panel (Get-BoardWindow) $panelId
        [System.Windows.Forms.SendKeys]::SendWait('^{RIGHT}')
        Start-Sleep -Milliseconds 400
    }
}

# With the gallery about to open: picks a card by title, then saves its form with the defaults it offers.
function Add-PanelFromGallery([string]$title) {
    $gallery = Wait-UrWindow '^Add a panel$' 15
    if (-not $gallery) { throw 'the gallery did not open' }
    $add = Get-Button $gallery "Add $title"
    if (-not $add) { throw "the gallery has no card '$title'" }
    Invoke-Element $add
    $form = Wait-UrWindow "^Add $title$" 15
    if (-not $form) { throw "no form for '$title'" }
    Invoke-Element (Find-ByAutomationId $form 'SaveSettingsButton')
    Start-Sleep -Milliseconds 1000
}

# boards.json, one board per item (see Read-Sources for why this unrolls with foreach).
function Read-Boards {
    $file = Join-Path $UrData 'boards.json'
    if (-not (Test-Path $file)) { return @() }
    $text = Get-Content $file -Raw
    if (-not $text.Trim()) { return @() }
    foreach ($b in ($text | ConvertFrom-Json)) { $b }
}

function Get-PopOutWindows { @(Get-UrWindows | Where-Object { $_.Current.AutomationId -eq 'PopOutWindow' }) }

# A pop-out's panel is named "<boardId>/<slotId>" (S2-P.16), so a slot id such as 'StandingPanel1' is matched as
# the suffix and a board-qualified id in full is matched exactly. With one board, as every walk here has, the
# suffix names the same window it always did; with two, it is the first found, and a walk that cares which must
# pass the qualified id. One definition, used by every walk: two of them used to carry their own copy.
function Find-PopOutPanel($window, [string]$panelId) {
    if (-not $window) { return $null }
    @($window.FindAll($TS::Descendants, $Cond::TrueCondition)) | Where-Object {
        $id = $_.Current.AutomationId
        $id -eq $panelId -or $id.EndsWith('/' + $panelId)
    } | Select-Object -First 1
}

function Get-PopOutFor([string]$panelId) {
    Get-PopOutWindows | Where-Object { Find-PopOutPanel $_ $panelId } | Select-Object -First 1
}

# Quits by closing the board window, so a pop-out is never mistaken for the main window.
function Stop-UrScoreFromBoard([int]$seconds = 20) {
    $board = Get-BoardWindow
    if ($board) { Close-UrWindow $board }
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-UrProcessId) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 400 }
    Stop-UrScore
}

# A clean start with the clan fixture imported (Points shown and sent), the main clan, and optionally a clan
# your accounts are in. Returns the board once its first Clan standing panel has drawn.
function Initialize-ClanBoard([string]$Main, [string]$Alt) {
    Start-UrScore | Out-Null
    Invoke-Element (Find-ByAutomationId (Get-BoardWindow) 'SetupButton')
    Wait-UrWindow '^Setup$' 15 | Out-Null
    Complete-ClanImport (Join-Path $UrFixtures 'petsim99-clan-battle.recipe.json') @('Points') @('Points') | Out-Null
    $setup = Wait-UrWindow '^Setup$' 30
    Select-SearchName $setup 'Your main clan' $Main
    Wait-Line $setup 'MainFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    if ($Alt) {
        Invoke-Element (Find-ByAutomationId $setup 'AddMineButton')
        Select-SearchName $setup 'Add a clan your accounts are in' $Alt
        Wait-Line $setup 'MineFoundLine' '^(Found |None of your accounts|Read |Added )' 120 | Out-Null
    }
    Close-UrWindow (Get-SetupWindow)
    Wait-Until {
        $p = Find-ByAutomationId (Get-BoardWindow) 'StandingPanel1'
        $p -and (Line $p 'PanelTitle') -eq 'Clan standing'
    } 30 | Out-Null
    return Get-BoardWindow
}
