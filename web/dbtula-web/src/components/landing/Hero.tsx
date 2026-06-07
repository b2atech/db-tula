import React from 'react'

type Props = {
  /** The live <GoogleLogin> button is rendered here (single source on the page). */
  signIn: React.ReactNode
}

export const Hero: React.FC<Props> = ({ signIn }) => {
  return (
    <section id="hero" className="py-16">
      <div className="mx-auto grid max-w-6xl items-center gap-10 px-4 lg:grid-cols-2">
        <div className="space-y-4">
          <p className="text-sm font-medium text-brand-orange">Schema integrity, automated</p>
          <h1 className="text-4xl font-bold tracking-tight text-text-primary md:text-5xl">
            Catch schema drift before it breaks production.
          </h1>
          <p className="text-lg text-text-secondary">
            Compare database schemas across QA, staging, and production — tables, columns,
            indexes, constraints, and routines. See exactly what changed and generate the
            sync script to close the gap.
          </p>

          <div className="pt-2">{signIn}</div>
        </div>

        <div>
          <div className="rounded-2xl border border-border-soft bg-bg-card p-8 shadow-sm">
            <SchemaCompareIllustration />
          </div>
        </div>
      </div>
    </section>
  )
}

// Two schema stacks with a comparison/diff motif between them.
const SchemaCompareIllustration: React.FC = () => (
  <svg viewBox="0 0 320 200" width="100%" height="100%" xmlns="http://www.w3.org/2000/svg" aria-hidden>
    <g fill="none" stroke="#F97316" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
      {/* Source schema (left) */}
      <rect x="24" y="40" width="96" height="120" rx="8" />
      <line x1="24" y1="64" x2="120" y2="64" />
      <line x1="40" y1="88" x2="104" y2="88" />
      <line x1="40" y1="108" x2="104" y2="108" />
      <line x1="40" y1="128" x2="80" y2="128" />

      {/* Target schema (right) */}
      <rect x="200" y="40" width="96" height="120" rx="8" />
      <line x1="200" y1="64" x2="296" y2="64" />
      <line x1="216" y1="88" x2="280" y2="88" />
      <line x1="216" y1="108" x2="280" y2="108" />
    </g>

    {/* Drift highlight on the target's missing row */}
    <g stroke="#F59E0B" strokeWidth="1.5" strokeLinecap="round">
      <line x1="216" y1="128" x2="240" y2="128" strokeDasharray="3 4" />
      <line x1="248" y1="128" x2="280" y2="128" strokeDasharray="3 4" />
    </g>

    {/* Comparison node in the middle */}
    <circle cx="160" cy="100" r="22" fill="#1E2A3A" stroke="#F97316" strokeWidth="1.5" />
    <g stroke="#F97316" strokeWidth="1.5" fill="none" strokeLinecap="round" strokeLinejoin="round">
      <path d="M150 94h14l-4-4" />
      <path d="M170 106h-14l4 4" />
    </g>
    <path d="M120 100h18" stroke="#F97316" strokeWidth="1.5" />
    <path d="M182 100h18" stroke="#F97316" strokeWidth="1.5" />
  </svg>
)

export default Hero
