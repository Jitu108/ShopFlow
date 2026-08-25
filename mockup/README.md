# ShopFlow design mockup

`shopflow-prototype.html` is the static reference mockup the current UI redesign (login,
register, catalog, navbar) is being matched against. Open it directly in a browser — it's a
single self-contained HTML/CSS/JS file, no build step. Its `[data-page]` tabs (top strip) switch
between the different mocked pages: catalog, product detail, cart, checkout, sign-in, vendor
dashboard, admin.

## Color tokens

Pulled from the source site's actual `:root` CSS (not guessed):

```css
--color-cream: #fff5e9;   /* page background */
--color-white: #fafafa;   /* card / surface background */
--color-ink:   #292929;   /* primary text, primary actions (outline style) */
--color-sage:  #587364;   /* accent — badges, prices, stars, links */
--color-line:  #dbdbdb;   /* borders */
--color-muted: #a0a0a0;   /* faint / secondary text */
--radius: 8px;
--max: 1320px;
--font-sans: "Proxima Soft", "Nunito Sans", ui-sans-serif, system-ui, -apple-system,
             BlinkMacSystemFont, "Segoe UI", sans-serif;
```

`Proxima Soft` is a paid font we can't load — `Nunito Sans` (Google Fonts) is the real fallback
used across the app.

## Applying it to real pages

Real ShopFlow pages built from this mockup (`ClientApp/src/app/login`, `register`,
`customer/catalog/catalog-list`) use **plain HTML controls**, not `mat-form-field`/`mat-button` —
Angular Material's own outline/shape/typography tokens fought an exact visual match on radius,
control height, and font. Business logic (Reactive Forms, NgRx dispatch, route guards) is
unchanged; only the presentation markup/CSS was swapped. Each such component scopes its own
copy of these tokens locally (see `:host { --auth-*: ...; }` in `login.scss` /
`--cat-*: ...` in `catalog-list.scss`) rather than touching the app-wide Material theme in
`ClientApp/src/styles.scss`.
