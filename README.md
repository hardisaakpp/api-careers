# api-careers

API del módulo **Careers** de Stockline (.NET 10, SQL Server): la hoja de vida de cada persona, las ofertas de
empleo, el ranking por afinidad y las postulaciones que prepara. El frontend es un módulo de
[stockline-web](../stockline-web); la inteligencia vive en [careers-ai](../careers-ai) (Python, sin estado).

```
stockline-web ──token de api-stockline──▶ api-careers ──X-Api-Key──▶ careers-ai ──▶ Ollama / LaTeX
   (módulo Careers)                       (datos, estados)            (ranking, adaptar, carta, PDF)
```

## Qué hace

| Endpoint | |
|---|---|
| `GET/PUT /cv` | La hoja de vida como documento. Solo el nombre es obligatorio; hay límites de tamaño y los enlaces deben ser http(s). |
| `POST /cv/pdf` | PDF del borrador que se está editando (no se guarda) o, sin cuerpo, de la hoja de vida guardada. |
| `GET/PUT /preferences` | Reglas duras que descartan ofertas: títulos, textos, ubicaciones, tecnologías obligatorias. |
| `POST /offers/refresh` | Consulta Remotive, Remote OK y We Work Remotely. Cada fuente, como máximo una vez cada 6 h (`?force=true` lo salta). Una fuente que falla no frena a las demás. |
| `GET /offers` | Ofertas ordenadas por cercanía a **tu** hoja de vida, con qué tecnologías coinciden, dónde aplicar y tu estado en cada una. `pending` indica ofertas cuya afinidad aún se calcula. |
| `POST /applications` | Prepara el borrador para una oferta: hoja de vida adaptada + carta. Repetirlo devuelve el mismo borrador. |
| `GET /applications[/{id}]`, `PUT /applications/{id}/letter`, `GET /applications/{id}/cv.pdf` | Seguimiento, carta editable y el PDF **exacto** que se revisó. |
| `POST /applications/{id}/confirm \| sent \| discard` | Estados `Draft → Confirmed → Sent`, o `Discarded`. No se salta la confirmación y lo enviado es definitivo. |

Los errores llevan un `code` estable (`cv-missing`, `invalid-transition`, `ai-unavailable`, `validation`, …) que el
frontend traduce.

## Decisiones

- **Un solo inicio de sesión.** Valida los tokens que emite api-stockline (misma clave, emisor y audiencia); no tiene
  usuarios propios ni emite tokens. Todo se filtra por el `sub` del token: la hoja de vida, las reglas y las
  postulaciones son de cada persona, y la de otra persona es indistinguible de una que no existe (404).
- **No envía nada.** La persona aplica en la página de la oferta y lo registra aquí.
- **Las ofertas son texto de terceros.** Se convierten a texto plano al entrar (sin scripts ni marcado), solo se aceptan
  enlaces http(s), los feeds se leen por https con tope de tamaño y los XML con DTD se rechazan. Nada de ese texto se
  trata como instrucciones: la adaptación solo elige y ordena lo que ya está en tu hoja de vida, y la carta es una plantilla.
- **La IA es opcional para el resto.** Si careers-ai no responde, solo fallan las operaciones que la usan (503 con
  `ai-unavailable`) y no se guarda nada a medias.
- **Escalable por diseño:** capas Domain / Application / Infrastructure / Api como api-stockline, repositorios detrás de
  puertos, y la hoja de vida y las reglas se guardan como documento (se editan enteras, no se consultan por campo).

## Ejecutar

```bash
cp .env.example .env            # SQL_PASSWORD, JWT_KEY (la de api-stockline), CAREERS_AI_KEY (la de careers-ai)
docker compose up -d --build    # :5190, reinicio automático; en Development crea la base `careers`
dotnet test                     # 59 pruebas, sin red ni IA reales
```

Necesita careers-ai en marcha (`../careers-ai/dev.sh`, :8930).
