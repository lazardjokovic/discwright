# Testing by hand

`test-matrix.html` is the pass somebody works through with the real program in front of them. Open it in a browser; it is one file and needs nothing else. Ticks are kept in that browser, so a pass can be left half finished and picked up later.

It covers what the automated suite cannot reach: whether the window changes when it should, whether a control is live when it should be and dead when it should not, whether a sentence still reads right at none and at one as well as at many, and the whole of the menu on the disc, which is an HTA and therefore invisible to UI Automation.

## Keeping it true

A list like this is worth nothing the moment it stops matching the app, and keeping it current by remembering to is the part that does not happen. So it is held to the windows by `TheTestMatrix` in the test project:

- every control that a person operates and that carries an `AutomationId` has to be named in the matrix, inside the `data-controls` attribute of the row that covers it;
- the matrix must not name a control no window has any more;
- and it has to stay a page a browser will open on its own.

So adding a button to a window and not saying when it is live breaks the build. That is the only mechanism that has ever kept a document up to date.

The `data-controls` attribute is invisible on the page. It exists only so the guard can tie a row to the thing it is about.

## When it finds something

Three of the sections are about faults that are known and not fixed, and they say so where they appear: the inputs that stay live during a run, the launcher that is never asked for, and the unfinished download that is detected and never mentioned. Section L lists them together, so a pass does not spend time reporting them again.
