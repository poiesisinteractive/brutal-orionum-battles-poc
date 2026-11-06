# Références du proto TS

Ces scripts tournent sur le proto TS au commit de référence `3eae78c` et produisent les fichiers de `tests/OrionumFleet.Sim.Tests/Reference/`, auxquels les tests C# se comparent.

## Préparer une copie du proto

Depuis le dépôt `orionum-battles`, dans un dossier de travail à part (pas dans le dépôt Unity) :

```sh
git -C <chemin>/orionum-battles archive 3eae78c fleet-proto/src fleet-proto/test fleet-proto/package.json fleet-proto/package-lock.json fleet-proto/tsconfig.json | tar -x -C <dossier>
cd <dossier>/fleet-proto
npm ci --ignore-scripts
```

## Régénérer les références de S1

Copier `dump-setup.ts` dans `<dossier>/fleet-proto`, puis :

```sh
npx tsx dump-setup.ts 42 wolf359-raid on > setup-raid-42.json
npx tsx dump-setup.ts 7 wolf359-raid on > setup-raid-7.json
npx tsx dump-setup.ts 3 well-field on > setup-wellfield-3.json
npx tsx dump-setup.ts 5 wolf359-raid off > setup-raid-5-noterrain.json
```

## Régénérer les références de S2

Copier `dump-flight.ts` dans `<dossier>/fleet-proto`, puis :

```sh
npx tsx dump-flight.ts > flight.json
```

Il joue cinq manœuvres scriptées, sans tir : déplacement d'un capital, repli combatif d'une frégate, Lanciers en ligne, escorte et passe balistique, tenailles. Il échantillonne positions et nez chaque seconde.
