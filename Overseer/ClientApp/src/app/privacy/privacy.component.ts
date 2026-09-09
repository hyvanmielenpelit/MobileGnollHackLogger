import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterModule } from '@angular/router';

/** A heading the table of contents links to. The `id` is the section element's own id. */
export interface PrivacySection {
  id: string;
  title: string;
}

/** One rung of the data sensitivity ladder, with the verdict the framework declares for it. */
export interface SensitivityRung {
  tier: string;
  meaning: string;
  verdict: string;
  /** True for the single rung that is the declared ceiling of what the product claims. */
  isCeiling: boolean;
}

/** One row of the data map: a kind of content, and what happens to it. */
export interface DataMapRow {
  what: string;
  storedWhere: string;
  howLong: string;
  sharedWith: string;
}

/** One row of the limitations matrix: a protection, and the edge it stops at. */
export interface LimitationRow {
  protection: string;
  /** Opens with the plain statement of the limit, so the sentence stands on its own. */
  limit: string;
}

/** One group of metadata that stays plaintext, named by what a reader could learn from it. */
export interface ReadableCategory {
  label: string;
  exposes: string;
  /** True for the single entry with the largest consequence for the reader. */
  isPrimary: boolean;
}

/** A third party that can receive data, and the conditions under which it does. */
export interface Subprocessor {
  name: string;
  receives: string;
  why: string;
  when: string;
}

/** A statement only the deployment's operator can make, rendered as visibly unfilled. */
export interface OperatorGap {
  title: string;
  detail: string;
}

@Component({
  selector: 'app-privacy',
  imports: [RouterModule],
  templateUrl: './privacy.component.html',
  styleUrl: './privacy.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PrivacyComponent {
  /** Drives the table of contents. Each id matches a `<section>` in the template. */
  readonly sections: readonly PrivacySection[] = [
    { id: 'data-sensitivity-ladder', title: 'The data sensitivity ladder' },
    { id: 'conversation-data-map', title: 'What Overseer does with a conversation' },
    { id: 'limitations', title: 'What these protections do not reach' },
    { id: 'readable-metadata', title: 'What stays readable even in a confidential chat' },
    { id: 'subprocessors', title: 'Subprocessors' },
    { id: 'operator-gaps', title: 'Still to be supplied by the operator' }
  ];

  readonly ladder: readonly SensitivityRung[] = [
    {
      tier: 'Public',
      meaning: 'Game data, wiki content, public leaderboard data.',
      verdict: 'Supported.',
      isCeiling: false
    },
    {
      tier: 'Your own personal data',
      meaning: 'Your own notes, logs and documents about yourself.',
      verdict: 'Yes — this is the declared ceiling.',
      isCeiling: true
    },
    {
      tier: 'Other people’s personal data',
      meaning: 'Documents identifying other people.',
      verdict: 'Protected identically in technical terms, but not claimed.',
      isCeiling: false
    },
    {
      tier: 'Regulated data',
      meaning: 'Health, financial and other special-category data.',
      verdict: 'Not supported.',
      isCeiling: false
    }
  ];

  readonly dataMap: readonly DataMapRow[] = [
    {
      what: 'The message you type',
      storedWhere: 'A row in Overseer’s database. In a normal chat it is plain text. In a chat you mark confidential it is enveloped under a key belonging to that chat alone. In an incognito chat it is held in server memory only and no row is ever written.',
      howLong: 'A normal chat: 90 days by default, and deleting it moves it to a 30-day trash first. A confidential chat: 30 days by default, and deleting it purges it straight away. An incognito chat: until you close it, or until it times out through inactivity — 60 minutes by default.',
      sharedWith: 'The AI provider that answers the turn.'
    },
    {
      what: 'Documents and images you attach',
      storedWhere: 'The bytes go to a file on the server under a generated name; the name you gave the file is kept as display data only. Those bytes are unencrypted in a normal chat, enveloped in a confidential one, and never leave memory in an incognito one. Text extracted from a document is also appended to the message body, so a document’s text exists twice.',
      howLong: 'The same as the chat it belongs to. Deleting the chat deletes the files.',
      sharedWith: 'The AI provider, on the turn you attach the file. A document under the direct-ingestion threshold — 12,000 tokens by default — is sent whole; a larger one is sent as ranked excerpts, and the turn says how many excerpts of how many you got. A document is single-turn: ask a follow-up and the assistant no longer has the file.'
    },
    {
      what: 'The call to the AI provider',
      storedWhere: 'Not kept as a record of its own. What Overseer stores is the chat rows above.',
      howLong: 'How long the provider keeps it is the provider’s own term, not a setting in Overseer. Overseer records a retention posture against each key and reports it on the chat, but recording an agreement is not enforcing one.',
      sharedWith: 'OpenAI, Anthropic or Google — whichever key funded the turn. Secrets with a recognisable shape are replaced with placeholders before the prompt leaves the server, and put back in the reply so you see your own values.'
    },
    {
      what: 'Error telemetry',
      storedWhere: 'Sent to Sentry when something fails. Credential-bearing headers, named query values and the whole user record are stripped first, personally identifying data is not attached by default, and request bodies are never captured.',
      howLong: 'Not stated in this notice — see “Still to be supplied by the operator”.',
      sharedWith: 'Sentry. Nothing at all from a confidential or an incognito turn: those events are dropped on the server and suppressed in the browser.'
    },
    {
      what: 'Cost and token accounting',
      storedWhere: 'Plain text beside the chat in every mode: token counts, an estimated cost and the pricing source it came from for each message, and a running total for the chat.',
      howLong: 'As long as the chat row. An incognito turn writes no message row, but it does record its token usage against the AI configuration that paid for it — a record that names no chat and no message, and is kept against that configuration rather than against your chat.',
      sharedWith: 'Nobody outside Overseer.'
    }
  ];

  readonly limitations: readonly LimitationRow[] = [
    {
      protection: 'Everything Overseer does, in every mode',
      limit: 'The provider still sees the conversation. Nothing here changes what is sent to OpenAI, Anthropic or Google for the model to answer.'
    },
    {
      protection: 'The retention posture recorded against each provider key, and the badge that reports it',
      limit: 'A provider’s retention posture is a claim about a contract, not a technical control. A zero-retention posture means an administrator read an agreement and dated it. It does not mean Overseer watched the provider delete anything, and no software can make it mean that.'
    },
    {
      protection: 'Envelope encryption of chat content at rest',
      limit: 'A normal chat’s content is stored in plain text. Encryption is confidential-only, and deliberately so: that is what keeps server-side search, game-snapshot detection and benchmark import working.'
    },
    {
      protection: 'Crypto-shredding, which destroys a chat’s key and makes its content permanently unreadable',
      limit: 'Crypto-shredding does not reach a backup taken before the shred. That backup still contains the wrapped key, and the master key is still in configuration, so it stays readable. Any claim that shredding reaches existing backup media is false.'
    },
    {
      protection: 'Incognito, which writes no chat, message, tool-call or attachment row and puts no file on disk',
      limit: 'An incognito chat is not saved by Overseer; that does not make it private. The conversation still reaches the AI provider, and RAM is not a legal or forensic boundary — process memory can be paged out by the operating system, captured in a crash dump, or read by anything with sufficient access to the server.'
    },
    {
      protection: 'Outbound masking, which replaces a recognised secret with a placeholder before the prompt leaves the server',
      limit: 'Outbound secret masking is a reduction, not a guarantee. A secret with no recognisable shape — a password, an internal hostname, a customer name, a bespoke token format — passes straight through, and nothing here detects it. Masking also covers only the copy that leaves the process: the database holds your text unmasked, by design.'
    },
    {
      protection: 'The boundary that wraps every uploaded document, and the standing instruction that text inside it is data which can never instruct',
      limit: 'Prompt injection is bounded, not prevented. The structural attack is gone — a document can no longer escape into the prompt’s own frame — but a persuasive injection inside a correctly wrapped document can still talk a model into misbehaving.'
    },
    {
      protection: 'Retrieval, which sends only the excerpts relevant to your question when a document is large',
      limit: 'Retrieval sends fewer parts of a document, not none. The excerpts it picks are the parts most relevant to the question, which is to say the parts most worth protecting.'
    },
    {
      protection: 'Malware scanning of every uploaded file before it is written',
      limit: 'Malware scanning is best-effort. It is only as good as the engine registered on the host, and a deployment may be running with no scanner at all.'
    },
    {
      protection: 'The data sensitivity ladder above',
      limit: 'No regulated-data support. There is no business associate agreement or equivalent with any AI provider, and no certification. Health, financial and other special-category data sit outside what Overseer claims to hold.'
    },
    {
      protection: 'The master key that wraps each confidential chat’s own key',
      limit: 'The master key lives in configuration, not in hardware. It can be rotated, but anyone who can read both the application’s configuration and its database can read a confidential chat.'
    },
    {
      protection: 'Turning Confidentiality Mode on for a chat that already exists',
      limit: 'Upgrading a chat is not retroactive. Earlier turns stay exactly as they were stored, so a confidential chat can legitimately hold plaintext rows and unencrypted attachment files from before the upgrade.'
    },
    {
      protection: 'Generated filenames, so the file system never holds the name you uploaded',
      limit: 'Attachments in a normal chat are stored unencrypted on disk. The name no longer leaks, but the bytes are readable to anyone with file access.'
    },
    {
      protection: 'Attachment serving that renders only images inline, never echoes an unlisted content type, and sends a no-sniff header',
      limit: 'Attachments are served from the application’s own origin. Serving them from a separate sandboxed origin is the stronger control, and it is deferred because it needs a hostname and certificate decision that belongs to the deployment.'
    },
    {
      protection: 'Managed document readers that execute nothing, strip and report active content, and bound every parse',
      limit: 'A document parser is an attack surface, and a bounded one is still one. A malformed file reaching a parser is a larger surface than a malformed file read as plain text, which is why the accepted formats are an allowlist. Note also that a PDF’s embedded actions are reported, not neutralised — the reader never runs them, and telling you they were there is the whole of the value.'
    },
    {
      protection: 'The guard on a custom provider endpoint, which refuses anything but an allowlisted host over HTTPS',
      limit: 'A custom endpoint is validated, not trusted. The guard bounds where the server may be directed; it says nothing about what runs there. An allowlisted host is a host the operator vouched for.'
    },
    {
      protection: 'The address check that runs when an administrator saves a custom endpoint',
      limit: 'DNS rebinding between validation and use is not prevented. The host allowlist is the control that matters here: an attacker must already control a host an operator named.'
    }
  ];

  readonly readableCategories: readonly ReadableCategory[] = [
    {
      label: 'Which provider saw the content',
      exposes: 'Every message records the provider it went to, the model that answered, that model’s display name and the service tier it ran under. That says which company received your words, even where nobody can read the words themselves.',
      isPrimary: true
    },
    {
      label: 'Timing',
      exposes: 'When the chat was created, when it was last used, when each message was sent, how long the model took to say its first word and how long it took to finish.',
      isPrimary: false
    },
    {
      label: 'Volume',
      exposes: 'Input, output and cache token counts for each message, and how many messages, tool calls and attachments a chat holds.',
      isPrimary: false
    },
    {
      label: 'Cost',
      exposes: 'The estimated cost of each message, the pricing source that produced the estimate, and the running total for the chat.',
      isPrimary: false
    },
    {
      label: 'Structure',
      exposes: 'Whether a message is yours or the assistant’s, whether it is hidden, whether it is a game snapshot or a replayed history, and the order of everything.',
      isPrimary: false
    },
    {
      label: 'Tool activity',
      exposes: 'Each tool call’s name and display name, its status, how long it waited and how long it ran, its place in a batch, its depth and which sub-agent ran it. Only its arguments, its result and its error text are encrypted.',
      isPrimary: false
    },
    {
      label: 'Attachment shape',
      exposes: 'How many files a message carries, the declared content type of each — which leaks image versus document, a cost accepted deliberately — and where the file sits on disk. Only the name you gave a file and its bytes are encrypted.',
      isPrimary: false
    },
    {
      label: 'Client state',
      exposes: 'The client settings stored on the chat, and the chat’s own privacy flags: whether it is confidential, when it was upgraded, the retention it carries and whether it purges immediately on deletion.',
      isPrimary: false
    }
  ];

  readonly subprocessors: readonly Subprocessor[] = [
    {
      name: 'OpenAI',
      receives: 'The prompt assembled for a turn: your message, the text of the documents attached to it, the replayed history of the chat and the results of any tools that ran.',
      why: 'To generate the reply.',
      when: 'Only when a key for it is configured and the turn is funded by that key.'
    },
    {
      name: 'Anthropic',
      receives: 'The prompt assembled for a turn: your message, the text of the documents attached to it, the replayed history of the chat and the results of any tools that ran.',
      why: 'To generate the reply.',
      when: 'Only when a key for it is configured and the turn is funded by that key.'
    },
    {
      name: 'Google',
      receives: 'The prompt assembled for a turn: your message, the text of the documents attached to it, the replayed history of the chat and the results of any tools that ran.',
      why: 'To generate the reply.',
      when: 'Only when a key for it is configured and the turn is funded by that key.'
    },
    {
      name: 'Sentry',
      receives: 'Error telemetry: the fault, its technical context and a trail of what the application was doing. Credential-bearing headers, named query values and the whole user record are stripped, and request bodies are never captured.',
      why: 'So faults can be diagnosed and fixed.',
      when: 'Only when something fails — and never from a confidential or an incognito turn, which are dropped on the server and suppressed in the browser.'
    },
    {
      name: 'Azure Communication Services',
      receives: 'Transactional e-mail: the address it goes to and the body of the message. Reporting a message sends that message, the surrounding conversation and your own name and address to an operator mailbox.',
      why: 'To deliver account e-mail, and to carry a message you choose to report.',
      when: 'When you report a message, or when the account system sends you e-mail. Reporting is refused outright in a confidential chat.'
    },
    {
      name: 'GitHub',
      receives: 'Whatever a tool call sends it — a search query, or a repository reference.',
      why: 'Two tools look things up on GitHub on the assistant’s behalf.',
      when: 'Only when the assistant uses one of those tools. This is the one category a confidential chat blocks: every tool that leaves the machine is withheld from the model and refused if it is asked for anyway.'
    }
  ];

  readonly operatorGaps: readonly OperatorGap[] = [
    {
      title: 'The legal entity responsible for this deployment',
      detail: 'Who is accountable for the data described on this page, and under which jurisdiction.'
    },
    {
      title: 'A contact address for data-subject requests',
      detail: 'Where to write to ask for access to your data, its correction, its erasure, a copy of it, or to object to its processing.'
    },
    {
      title: 'Whether a data processing agreement is offered, and on what terms',
      detail: 'This page does not state that one exists.'
    },
    {
      title: 'How a personal-data breach is assessed and notified, and within what time',
      detail: 'This page does not state a notification process or a deadline.'
    },
    {
      title: 'Where data is stored and processed, and in which regions inference runs',
      detail: 'This page makes no data-residency guarantee, for Overseer’s own storage or for any provider.'
    },
    {
      title: 'How long error telemetry is kept',
      detail: 'Retention for the telemetry described in the data map above is set outside Overseer.'
    }
  ];
}
