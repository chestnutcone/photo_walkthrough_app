import 'package:flutter/widgets.dart';

/// [normal], or zero when the platform asks for reduced motion.
Duration motionDuration(BuildContext context, Duration normal) =>
    MediaQuery.disableAnimationsOf(context) ? Duration.zero : normal;
