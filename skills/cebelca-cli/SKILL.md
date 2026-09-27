---
name: cebelca-cli
description: Use the locally installed Čebelca CLI to inspect partners, invoices, invoice lines, payments, and PDFs, or to make explicitly requested Čebelca billing changes.
---

# Čebelca CLI

Use `cebelca-cli` for Čebelca accounting API tasks. The local launcher is installed at `~/.local/bin/cebelca-cli`. Run `cebelca-cli --help` for commands and `cebelca-cli <command> --help` for its options. The CLI accepts positional invoice IDs and named flags; do not make the user supply JSON arguments.

Common reads:

```bash
cebelca-cli invoice 258
cebelca-cli invoice-lines 258
cebelca-cli payments 258
cebelca-cli invoices
cebelca-cli partners
cebelca-cli invoice-pdf 258 --output /absolute/path/invoice.pdf
```

The CLI writes JSON to standard output. An invoice includes additional Čebelca properties in `fields`. For fiscalized invoices, `title` contains the invoice number assembled from `locreg` and `docnum`. Fetch line items separately with `invoice-lines`; do not assume an invoice header contains them. Deduplicate invoice IDs before reporting totals from `invoices`, and state which date field and range you used.

The launcher uses `CEBELCA_API_KEY` if already set, or the local `bws-touchid` helper. Let the helper prompt the user if needed. Never read, print, or store the API key in a skill, command, log, or response. Limit output containing customer data to what the task needs.

For changes such as creating or issuing an invoice, adding a payment, or emailing an invoice, act only when the user requested that change. Inspect the exact command's `--help` output and the target record first. If a write times out or returns an ambiguous result, check the remote state before retrying so you do not create duplicates.
