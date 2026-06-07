import React from 'react'
import logoUrl from '../../assets/dbtula.png'

type Props = {
  className?: string
}

// Full horizontal logo (icon + wordmark) on a dark background, so it blends into
// the dark landing page. Imported from src/assets so Vite fingerprints it.
export const Logo: React.FC<Props> = ({ className = '' }) => {
  return <img src={logoUrl} alt="dbtula" className={`h-12 w-auto ${className}`} />
}

export default Logo
